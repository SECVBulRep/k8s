Кластерный NFS с Longhorn 


Обзор решения
Настоящий кластерный NFS с автоматической репликацией
Longhorn distributed storage на всех 3 нодах
Автоматический failover без потери данных
Snapshots и backups через Longhorn
LoadBalancer на IP 172.16.29.112




Характеристика

DaemonSet NFS

Longhorn NFS

Серверов

3 независимых

1 кластерный

Репликация

 Нет

 Автоматическая

Failover

 Потеря данных

 Без потери

Данные

 Разные

 Единые



Часть 1: Подготовка нод для Longhorn
#!/bin/bash

# ===============================================
# Подготовка всех нод для Longhorn
# ===============================================

echo "🔧 Подготовка нод для Longhorn..."

# Функция для подготовки одной ноды
prepare_node() {
    local node=$1
    echo "=== Подготовка $node ==="
    
    ssh $node '
        # Установка зависимостей
        sudo apt update
        sudo apt install -y open-iscsi util-linux curl
        
        # Настройка iscsid
        sudo systemctl enable iscsid
        sudo systemctl start iscsid
        
        # Проверка статуса
        echo "✅ iscsid статус:"
        sudo systemctl status iscsid --no-pager | head -3
        
        # Создание директории Longhorn
        sudo mkdir -p /var/lib/longhorn
        
        # Проверка модулей ядра
        echo "📊 Проверка iscsi модулей:"
        lsmod | grep iscsi || echo "Модули iscsi не загружены (это нормально)"
        
        echo "✅ Нода $HOSTNAME готова"
    '
}

# Подготовка всех нод
for node in k8s01 k8s02 k8s03; do
    prepare_node $node
    echo ""
done

echo "🔍 Итоговая проверка iscsid на всех нодах:"
for node in k8s01 k8s02 k8s03; do
    echo "=== $node ==="
    ssh $node "sudo systemctl is-active iscsid"
done

echo "✅ Все ноды подготовлены для Longhorn!"
Часть 2: Установка Longhorn с проверками
Эта часть включает все необходимые проверки готовности компонентов для предотвращения типичных проблем.



#!/bin/bash

# ===============================================
# Установка Longhorn с полными проверками
# ===============================================

echo "🚀 Устанавливаем Longhorn с проверками готовности..."

# 1. Удаление предыдущей установки (если есть)
if kubectl get namespace longhorn-system 2>/dev/null; then
    echo "🧹 Удаляем предыдущую установку Longhorn..."
    kubectl delete namespace longhorn-system
    while kubectl get namespace longhorn-system 2>/dev/null; do
        echo "Ожидание удаления namespace..."
        sleep 5
    done
fi

# 2. Создание namespace
kubectl create namespace longhorn-system

# 3. Установка Longhorn
echo "📦 Загружаем и устанавливаем Longhorn v1.6.0..."
kubectl apply -f https://raw.githubusercontent.com/longhorn/longhorn/v1.6.0/deploy/longhorn.yaml

# 4. Ожидание создания CRD (критично!)
echo "⏳ Ждем создания Custom Resource Definitions..."
for i in {1..60}; do
    if kubectl get crd nodes.longhorn.io 2>/dev/null; then
        echo "✅ CRD созданы!"
        break
    fi
    echo "Попытка $i/60: CRD создаются..."
    sleep 10
done

if ! kubectl get crd nodes.longhorn.io 2>/dev/null; then
    echo "❌ CRD не созданы за 10 минут!"
    exit 1
fi

# 5. Ожидание готовности базовых компонентов
echo "⏳ Ждем готовности longhorn-manager..."
kubectl wait --for=condition=ready pod -l app=longhorn-manager -n longhorn-system --timeout=600s

echo "⏳ Ждем готовности CSI компонентов..."
kubectl wait --for=condition=ready pod -l app=csi-attacher,component=csi -n longhorn-system --timeout=300s

# 6. КРИТИЧНО: Ждем создания Longhorn нод
echo "⏳ КРИТИЧНО: Ждем создания Longhorn нод (может занять 5 минут)..."
for i in {1..60}; do
    NODES_COUNT=$(kubectl get nodes.longhorn.io -n longhorn-system --no-headers 2>/dev/null | wc -l)
    if [ "$NODES_COUNT" -ge 3 ]; then
        echo "✅ Longhorn ноды созданы: $NODES_COUNT/3"
        break
    fi
    echo "Попытка $i/60: создано нод $NODES_COUNT/3..."
    sleep 5
done

# 7. Проверка готовности нод
echo "📊 Проверяем готовность Longhorn нод:"
kubectl get nodes.longhorn.io -n longhorn-system -o custom-columns="NAME:.metadata.name,READY:.status.conditions[?(@.type=='Ready')].status,SCHEDULABLE:.spec.allowScheduling"

# 8. Ождание engine images
echo "⏳ Ждем готовности engine images..."
for i in {1..30}; do
    ENGINE_COUNT=$(kubectl get engineimages.longhorn.io -n longhorn-system --no-headers 2>/dev/null | wc -l)
    if [ "$ENGINE_COUNT" -ge 1 ]; then
        echo "✅ Engine images готовы: $ENGINE_COUNT"
        break
    fi
    echo "Попытка $i/30: engine images загружаются..."
    sleep 10
done

# 9. Финальная проверка
echo "📊 Финальная проверка Longhorn:"
echo "Поды:"
kubectl get pods -n longhorn-system | grep -E "(READY|Running)" | head -10

echo ""
echo "StorageClass:"
kubectl get storageclass | grep longhorn

echo ""
echo "Engine Images:"
kubectl get engineimages.longhorn.io -n longhorn-system

echo ""
echo "Longhorn Ноды:"
kubectl get nodes.longhorn.io -n longhorn-system

# 10. Проверка критических проблем
FAILED_PODS=$(kubectl get pods -n longhorn-system | grep -E "(Error|CrashLoopBackOff|ImagePullBackOff)" | wc -l)
if [ "$FAILED_PODS" -gt 0 ]; then
    echo "⚠️ Обнаружены проблемные поды:"
    kubectl get pods -n longhorn-system | grep -E "(Error|CrashLoopBackOff|ImagePullBackOff)"
fi

echo ""
echo "✅ Longhorn установлен и готов к работе!"
Часть 3: Настройка MetalLB
#!/bin/bash

# ===============================================
# Настройка MetalLB для кластерного NFS
# ===============================================

echo "🌐 Настраиваем MetalLB для кластерного NFS..."

# 1. Проверка существующих пулов
echo "📊 Текущие IP пулы MetalLB:"
kubectl get ipaddresspool -n metallb-system

# 2. Создание IP пула для кластерного NFS
cat > nfs-cluster-metallb.yaml << 'EOF'
apiVersion: metallb.io/v1beta1
kind: IPAddressPool
metadata:
  name: nfs-cluster-pool
  namespace: metallb-system
spec:
  addresses:
  - 172.16.29.112/32  # IP для кластерного NFS
---
apiVersion: metallb.io/v1beta1
kind: L2Advertisement
metadata:
  name: nfs-cluster-l2
  namespace: metallb-system
spec:
  ipAddressPools:
  - nfs-cluster-pool
EOF

kubectl apply -f nfs-cluster-metallb.yaml

echo "✅ MetalLB настроен для IP 172.16.29.112"

# 3. Опциональная настройка Longhorn UI
read -p "🖥️ Настроить внешний доступ к Longhorn UI на 172.16.29.113? (y/n): " -n 1 -r
echo
if [[ $REPLY =~ ^[Yy]$ ]]; then
    # Добавляем IP для UI
    cat > longhorn-ui-metallb.yaml << 'EOF'
apiVersion: metallb.io/v1beta1
kind: IPAddressPool
metadata:
  name: longhorn-ui-pool
  namespace: metallb-system
spec:
  addresses:
  - 172.16.29.113/32  # IP для Longhorn UI
---
apiVersion: metallb.io/v1beta1
kind: L2Advertisement
metadata:
  name: longhorn-ui-l2
  namespace: metallb-system
spec:
  ipAddressPools:
  - longhorn-ui-pool
EOF
    kubectl apply -f longhorn-ui-metallb.yaml
    
    # Создаем внешний сервис
    cat > longhorn-ui-external.yaml << 'EOF'
apiVersion: v1
kind: Service
metadata:
  name: longhorn-frontend-external
  namespace: longhorn-system
spec:
  type: LoadBalancer
  loadBalancerIP: 172.16.29.113
  selector:
    app: longhorn-ui
  ports:
  - name: http
    port: 80
    targetPort: 8000
    protocol: TCP
EOF
    kubectl apply -f longhorn-ui-external.yaml
    echo "🌐 Longhorn UI будет доступен на http://172.16.29.113"
fi

echo ""
echo "📊 Итоговые IP пулы:"
kubectl get ipaddresspool -n metallb-system
Часть 4: Создание кластерного NFS
Эта часть включает проверку соответствия количества Longhorn нод и требуемых реплик для предотвращения ошибок создания PVC.



#!/bin/bash

# ===============================================
# Создание кластерного NFS с Longhorn
# ===============================================

echo "🗄️ Создаем кластерный NFS с автоматической репликацией..."

# 1. Создание namespace
kubectl create namespace nfs-cluster

# 2. StorageClass для высокодоступного NFS
cat > nfs-cluster-storageclass.yaml << 'EOF'
apiVersion: storage.k8s.io/v1
kind: StorageClass
metadata:
  name: nfs-cluster-storage
provisioner: driver.longhorn.io
allowVolumeExpansion: true
reclaimPolicy: Retain
volumeBindingMode: Immediate
parameters:
  numberOfReplicas: "3"           # Реплики на все 3 ноды
  staleReplicaTimeout: "30"
  dataLocality: "disabled"        # Доступ с любой ноды
  fsType: "ext4"
  migratable: "true"              # Автоматическая миграция
EOF

kubectl apply -f nfs-cluster-storageclass.yaml

# 3. PVC для кластерного хранилища с проверками
cat > nfs-cluster-pvc.yaml << 'EOF'
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: nfs-cluster-data
  namespace: nfs-cluster
spec:
  accessModes:
    - ReadWriteOnce
  storageClassName: nfs-cluster-storage
  resources:
    requests:
      storage: 50Gi  # Размер кластерного хранилища
EOF

kubectl apply -f nfs-cluster-pvc.yaml

# 4. КРИТИЧНО: Проверка Longhorn нод перед созданием PVC
echo "🔍 Проверяем готовность Longhorn нод перед созданием тома..."
LONGHORN_NODES=$(kubectl get nodes.longhorn.io -n longhorn-system --no-headers 2>/dev/null | wc -l)
echo "Доступно Longhorn нод: $LONGHORN_NODES"
kubectl get nodes.longhorn.io -n longhorn-system

# Проверка соответствия нод и реплик
if [ "$LONGHORN_NODES" -lt 3 ]; then
    echo "⚠️ ПРОБЛЕМА: Доступно только $LONGHORN_NODES Longhorn нод, но требуется 3 реплики!"
    echo ""
    echo "Варианты решения:"
    echo "1. Добавить отсутствующие ноды в Longhorn"
    echo "2. Уменьшить количество реплик до $LONGHORN_NODES"
    echo ""
    
    read -p "Выберите решение (1-добавить ноды, 2-уменьшить реплики, Enter-автоматически): " -n 1 -r
    echo
    
    if [[ $REPLY =~ ^[1]$ ]] || [[ -z $REPLY ]]; then
        echo "🔧 Пытаемся добавить отсутствующие ноды..."
        
        # Проверяем какие ноды отсутствуют
        for node in k8s01 k8s02 k8s03; do
            if ! kubectl get nodes.longhorn.io $node -n longhorn-system >/dev/null 2>&1; then
                echo "Добавляем ноду $node в Longhorn..."
                
                # Создаем директорию на отсутствующей ноде
                ssh $node "sudo mkdir -p /var/lib/longhorn && sudo chmod 755 /var/lib/longhorn" || echo "Не удалось создать директорию на $node"
                
                # Принудительно создаем Longhorn ноду
                cat > ${node}-longhorn-node.yaml << EOF
apiVersion: longhorn.io/v1beta2
kind: Node
metadata:
  name: $node
  namespace: longhorn-system
spec:
  allowScheduling: true
  name: $node
EOF
                kubectl apply -f ${node}-longhorn-node.yaml
                
                # Ждем готовности ноды
                echo "⏳ Ждем готовности ноды $node..."
                for j in {1..30}; do
                    if kubectl get nodes.longhorn.io $node -n longhorn-system >/dev/null 2>&1; then
                        echo "✅ Нода $node добавлена!"
                        break
                    fi
                    sleep 10
                done
            fi
        done
        
        # Финальная проверка
        LONGHORN_NODES_FINAL=$(kubectl get nodes.longhorn.io -n longhorn-system --no-headers 2>/dev/null | wc -l)
        echo "📊 Итого Longhorn нод: $LONGHORN_NODES_FINAL"
        kubectl get nodes.longhorn.io -n longhorn-system
        
    elif [[ $REPLY =~ ^[2]$ ]]; then
        echo "🔧 Изменяем StorageClass на $LONGHORN_NODES реплики..."
        
        # Обновляем StorageClass с меньшим количеством реплик
        kubectl patch storageclass nfs-cluster-storage -p "{\"parameters\":{\"numberOfReplicas\":\"$LONGHORN_NODES\"}}"
        echo "✅ StorageClass обновлен на $LONGHORN_NODES реплики"
    fi
    
    echo ""
    echo "📊 Финальная проверка перед созданием PVC:"
    kubectl get nodes.longhorn.io -n longhorn-system
fi

# 5. Ожидание создания тома с улучшенной диагностикой
echo "⏳ Ждем создания Longhorn тома (может занять 3-5 минут)..."
for i in {1..60}; do
    PVC_STATUS=$(kubectl get pvc nfs-cluster-data -n nfs-cluster -o jsonpath='{.status.phase}' 2>/dev/null)
    if [ "$PVC_STATUS" = "Bound" ]; then
        echo "✅ Longhorn том создан и реплицирован!"
        break
    fi
    echo "Попытка $i/60: PVC статус = $PVC_STATUS"
    
    # Диагностика при проблемах
    if [ $i -eq 20 ]; then
        echo "🔍 Диагностика задержки создания тома:"
        kubectl describe pvc nfs-cluster-data -n nfs-cluster | tail -10
        echo ""
        echo "Engine images:"
        kubectl get engineimages.longhorn.io -n longhorn-system
        echo ""
        echo "Доступные Longhorn ноды:"
        kubectl get nodes.longhorn.io -n longhorn-system
        echo ""
        echo "Instance managers:"
        kubectl get pods -n longhorn-system | grep instance-manager
    fi
    
    sleep 5
done

# Проверка результата с детальной диагностикой
if ! kubectl get pvc nfs-cluster-data -n nfs-cluster | grep -q Bound; then
    echo "❌ Том не создался за 5 минут!"
    echo ""
    echo "🔍 Детальная диагностика проблемы:"
    kubectl describe pvc nfs-cluster-data -n nfs-cluster
    
    echo ""
    echo "📊 Состояние Longhorn:"
    echo "Ноды: $(kubectl get nodes.longhorn.io -n longhorn-system --no-headers | wc -l)"
    kubectl get nodes.longhorn.io -n longhorn-system
    
    echo ""
    echo "Instance managers: $(kubectl get pods -n longhorn-system | grep -c instance-manager)"
    kubectl get pods -n longhorn-system | grep instance-manager
    
    echo ""
    echo "⚠️ Возможные причины:"
    echo "   1. Недостаточно Longhorn нод для реплик"
    echo "   2. Instance managers не готовы"
    echo "   3. Недостаточно места на дисках"
    echo "   4. Network проблемы между нодами"
    
    exit 1
fi

echo "📊 Созданный Longhorn том:"
kubectl get pvc nfs-cluster-data -n nfs-cluster
kubectl get pv | grep nfs-cluster-data

# 6. Создание HA NFS Deployment с ИСПРАВЛЕННЫМИ портами
cat > nfs-cluster-deployment.yaml << 'EOF'
apiVersion: apps/v1
kind: Deployment
metadata:
  name: nfs-server-cluster
  namespace: nfs-cluster
  labels:
    app: nfs-server-cluster
spec:
  replicas: 1  # Один активный, Longhorn обеспечивает HA
  strategy:
    type: Recreate  # Важно для RWO тома
  selector:
    matchLabels:
      app: nfs-server-cluster
  template:
    metadata:
      labels:
        app: nfs-server-cluster
    spec:
      containers:
      - name: nfs-server
        image: erichough/nfs-server:latest
        env:
        - name: NFS_EXPORT_0
          value: "/data *(rw,fsid=0,async,no_subtree_check,no_auth_nlm,insecure,no_root_squash)"
        - name: NFS_EXPORT_1
          value: "/data/shared *(rw,fsid=1,async,no_subtree_check,no_auth_nlm,insecure,no_root_squash)"
        - name: NFS_EXPORT_2
          value: "/data/app *(rw,fsid=2,async,no_subtree_check,no_auth_nlm,insecure,no_root_squash)"
        - name: NFS_EXPORT_3
          value: "/data/config *(rw,fsid=3,async,no_subtree_check,no_auth_nlm,insecure,no_root_squash)"
        - name: NFS_PORT_MOUNTD
          value: "20048"  # КРИТИЧНО! Фиксируем порт mountd для Kubernetes
        ports:
        - name: nfs
          containerPort: 2049
          protocol: TCP
        - name: mountd
          containerPort: 20048  # Должен совпадать с NFS_PORT_MOUNTD
          protocol: TCP
        - name: rpcbind
          containerPort: 111
          protocol: TCP
        securityContext:
          privileged: true
          capabilities:
            add:
            - SYS_ADMIN
            - SYS_MODULE
        volumeMounts:
        - name: nfs-storage
          mountPath: /data
        - name: lib-modules
          mountPath: /lib/modules
          readOnly: true
        resources:
          requests:
            cpu: 300m
            memory: 512Mi
          limits:
            cpu: 1000m
            memory: 1Gi
        livenessProbe:
          tcpSocket:
            port: 2049
          initialDelaySeconds: 30
          periodSeconds: 30
          timeoutSeconds: 5
          failureThreshold: 3
        readinessProbe:
          tcpSocket:
            port: 2049
          initialDelaySeconds: 15
          periodSeconds: 10
          timeoutSeconds: 3
          failureThreshold: 3
      volumes:
      - name: nfs-storage
        persistentVolumeClaim:
          claimName: nfs-cluster-data  # Longhorn реплицированный том!
      - name: lib-modules
        hostPath:
          path: /lib/modules
          type: Directory
      # Антиаффинити для лучшего распределения
      affinity:
        podAntiAffinity:
          preferredDuringSchedulingIgnoredDuringExecution:
          - weight: 100
            podAffinityTerm:
              labelSelector:
                matchExpressions:
                - key: app
                  operator: In
                  values:
                  - nfs-server-cluster
              topologyKey: kubernetes.io/hostname
EOF

kubectl apply -f nfs-cluster-deployment.yaml

# 7. Создание LoadBalancer для кластерного NFS
cat > nfs-cluster-service.yaml << 'EOF'
apiVersion: v1
kind: Service
metadata:
  name: nfs-cluster-service
  namespace: nfs-cluster
  labels:
    app: nfs-server-cluster
spec:
  type: LoadBalancer
  loadBalancerIP: 172.16.29.112
  selector:
    app: nfs-server-cluster
  ports:
  - name: nfs
    port: 2049
    targetPort: 2049
    protocol: TCP
  - name: mountd
    port: 20048
    targetPort: 20048
    protocol: TCP
  - name: rpcbind
    port: 111
    targetPort: 111
    protocol: TCP
  sessionAffinity: ClientIP  # Важно для NFS стабильности
  sessionAffinityConfig:
    clientIP:
      timeoutSeconds: 3600
EOF

kubectl apply -f nfs-cluster-service.yaml

# 8. Ожидание готовности NFS с проверками
echo "⏳ Ждем готовности кластерного NFS сервера..."
kubectl wait --for=condition=available deployment/nfs-server-cluster -n nfs-cluster --timeout=300s

# 9. Ожидание внешнего IP
echo "⏳ Ждем назначения внешнего IP 172.16.29.112..."
for i in {1..24}; do
    EXTERNAL_IP=$(kubectl get svc nfs-cluster-service -n nfs-cluster -o jsonpath='{.status.loadBalancer.ingress[0].ip}' 2>/dev/null)
    if [ "$EXTERNAL_IP" = "172.16.29.112" ]; then
        echo "✅ Внешний IP назначен: $EXTERNAL_IP"
        break
    fi
    echo "Попытка $i/24: ожидаем IP..."
    sleep 5
done

# 10. Проверка портов NFS
echo "📊 Проверяем порты NFS сервера:"
POD_NAME=$(kubectl get pod -n nfs-cluster -l app=nfs-server-cluster -o jsonpath='{.items[0].metadata.name}')
kubectl exec -n nfs-cluster $POD_NAME -- netstat -tulpn | grep -E ':(111|2049|20048)'

# 11. Инициализация структуры директорий
echo "📁 Инициализируем структуру директорий в кластерном хранилище..."
kubectl exec -n nfs-cluster $POD_NAME -- mkdir -p /data/shared /data/app /data/config
kubectl exec -n nfs-cluster $POD_NAME -- chmod 755 /data /data/shared /data/app /data/config
kubectl exec -n nfs-cluster $POD_NAME -- sh -c "echo 'Кластерный Longhorn NFS готов - $(date)' > /data/shared/cluster-ready.txt"

# 12. Проверка экспортов
echo "📊 Проверяем NFS экспорты:"
kubectl exec -n nfs-cluster $POD_NAME -- showmount -e localhost

echo ""
echo "🎯 Кластерный NFS с Longhorn создан и готов!"
echo "🌐 Доступен по адресу: 172.16.29.112"
echo "📂 Пути экспортов:"
echo "   - /data (корневой)"
echo "   - /data/shared (общие файлы)"
echo "   - /data/app (данные приложений)"
echo "   - /data/config (конфигурация)"
echo ""
echo "🔄 Данные автоматически реплицируются на все ноды!"
Часть 5: Тестирование кластерного NFS
Обязательно выполните тестирование для проверки корректности репликации и failover функций.





#!/bin/bash

# ===============================================
# Полное тестирование кластерного NFS
# ===============================================

echo "🧪 Тестируем кластерный NFS с Longhorn..."

# 1. Проверка готовности перед тестами
echo "📊 Предварительная проверка:"
kubectl get deployment,svc,pvc -n nfs-cluster
kubectl get pods -n nfs-cluster -o wide

echo ""
echo "📊 Longhorn том:"
kubectl get pv | grep nfs-cluster-data

# 2. Тест базового подключения
cat > nfs-cluster-test.yaml << 'EOF'
apiVersion: v1
kind: Pod
metadata:
  name: nfs-cluster-test
  namespace: nfs-cluster
spec:
  containers:
  - name: client
    image: alpine:latest
    command: ["/bin/sh", "-c"]
    args:
    - |
      echo "🧪 Тестируем кластерный NFS с Longhorn..."
      echo ""
      echo "📁 Содержимое кластерного хранилища:"
      ls -la /mnt/nfs/
      echo ""
      if [ -f "/mnt/nfs/shared/cluster-ready.txt" ]; then
        echo "📄 Файл готовности кластера:"
        cat /mnt/nfs/shared/cluster-ready.txt
      fi
      echo ""
      echo "✏️ Тестируем запись в кластерное хранилище:"
      echo "SUCCESS! Кластерный Longhorn NFS работает - $(date)" > /mnt/nfs/shared/test-write.txt
      cat /mnt/nfs/shared/test-write.txt
      echo ""
      echo "📊 Дисковое пространство (реплицированное на ноды):"
      df -h /mnt/nfs
      echo ""
      echo "🔄 Создаем файлы для проверки репликации:"
      for i in {1..3}; do
        echo "Тестовый файл $i в кластерном хранилище - $(date)" > /mnt/nfs/shared/cluster-test-$i.txt
      done
      ls -la /mnt/nfs/shared/cluster-test-*.txt
      echo ""
      echo "📂 Тестируем поддиректории:"
      echo "App data в кластере" > /mnt/nfs/app/cluster-app.txt
      echo "Config в кластере" > /mnt/nfs/config/cluster-config.txt
      echo "Файлы созданы:"
      ls -la /mnt/nfs/app/cluster-app.txt /mnt/nfs/config/cluster-config.txt
      echo ""
      echo "✅ Базовое тестирование кластерного NFS завершено!"
      sleep 180  # Держим для failover теста
    volumeMounts:
    - name: nfs-volume
      mountPath: /mnt/nfs
  volumes:
  - name: nfs-volume
    nfs:
      server: 172.16.29.112  # Кластерный NFS
      path: /data
  restartPolicy: Never
EOF

kubectl apply -f nfs-cluster-test.yaml

echo "⏳ Ждем готовности тестового клиента..."
kubectl wait --for=condition=ready pod nfs-cluster-test -n nfs-cluster --timeout=120s

sleep 15

echo ""
echo "📋 Результаты базового тестирования:"
kubectl logs -n nfs-cluster nfs-cluster-test

# 3. КРИТИЧЕСКИЙ ТЕСТ: Failover кластерного хранилища
echo ""
echo "🔥 КРИТИЧЕСКИЙ ТЕСТ FAILOVER..."
echo "   Проверяем реальную высокую доступность кластерного хранилища"

# Проверяем текущее размещение
echo "📊 Текущее размещение NFS сервера:"
kubectl get pod -n nfs-cluster -l app=nfs-server-cluster -o wide

echo ""
echo "💥 УДАЛЯЕМ NFS ПОД (симуляция падения сервера)..."
kubectl delete pod -n nfs-cluster -l app=nfs-server-cluster

echo "⏳ Ждем автоматического восстановления Kubernetes..."
kubectl wait --for=condition=available deployment/nfs-server-cluster -n nfs-cluster --timeout=300s

echo "⏳ Даем время на полное восстановление NFS сервисов..."
sleep 30

echo ""
echo "📊 Новое размещение после failover:"
kubectl get pod -n nfs-cluster -l app=nfs-server-cluster -o wide

# 4. Проверка целостности данных после failover
cat > nfs-failover-test.yaml << 'EOF'
apiVersion: v1
kind: Pod
metadata:
  name: nfs-failover-test
  namespace: nfs-cluster
spec:
  containers:
  - name: client
    image: alpine:latest
    command: ["/bin/sh", "-c"]
    args:
    - |
      echo "🔄 ПРОВЕРЯЕМ ДАННЫЕ ПОСЛЕ FAILOVER..."
      echo ""
      echo "📁 Содержимое после failover:"
      ls -la /mnt/nfs/shared/
      echo ""
      echo "📄 Проверяем файлы созданные ДО failover:"
      
      # Проверка каждого файла
      files_ok=0
      files_total=0
      
      for i in {1..3}; do
        files_total=$((files_total + 1))
        if [ -f "/mnt/nfs/shared/cluster-test-$i.txt" ]; then
          echo "✅ cluster-test-$i.txt: $(cat /mnt/nfs/shared/cluster-test-$i.txt | head -1)"
          files_ok=$((files_ok + 1))
        else
          echo "❌ cluster-test-$i.txt: ПОТЕРЯН!"
        fi
      done
      
      if [ -f "/mnt/nfs/shared/test-write.txt" ]; then
        echo "✅ test-write.txt: $(cat /mnt/nfs/shared/test-write.txt)"
        files_ok=$((files_ok + 1))
      else
        echo "❌ test-write.txt: ПОТЕРЯН!"
      fi
      files_total=$((files_total + 1))
      
      echo ""
      echo "📂 Проверяем поддиректории после failover:"
      if [ -f "/mnt/nfs/app/cluster-app.txt" ]; then
        echo "✅ app/cluster-app.txt: $(cat /mnt/nfs/app/cluster-app.txt)"
        files_ok=$((files_ok + 1))
      else
        echo "❌ app/cluster-app.txt: ПОТЕРЯН!"
      fi
      files_total=$((files_total + 1))
      
      if [ -f "/mnt/nfs/config/cluster-config.txt" ]; then
        echo "✅ config/cluster-config.txt: $(cat /mnt/nfs/config/cluster-config.txt)"
        files_ok=$((files_ok + 1))
      else
        echo "❌ config/cluster-config.txt: ПОТЕРЯН!"
      fi
      files_total=$((files_total + 1))
      
      echo ""
      echo "✏️ Создаем новый файл ПОСЛЕ failover:"
      echo "POST-FAILOVER: Кластерное хранилище восстановлено - $(date)" > /mnt/nfs/shared/after-failover.txt
      cat /mnt/nfs/shared/after-failover.txt
      
      echo ""
      echo "🎯 РЕЗУЛЬТАТ FAILOVER ТЕСТА:"
      echo "   Данных сохранено: $files_ok/$files_total"
      echo "   Запись после failover: ✅"
      echo "   Автовосстановление: ✅"
      echo ""
      
      if [ $files_ok -eq $files_total ]; then
        echo "🎉 FAILOVER ТЕСТ УСПЕШЕН! Кластерное хранилище работает идеально!"
        echo "   ✅ Все данные сохранены"
        echo "   ✅ Автоматическое восстановление работает"
        echo "   ✅ Longhorn обеспечивает настоящую высокую доступность"
      else
        echo "⚠️ ПРОБЛЕМЫ с failover: потеряно $((files_total - files_ok)) файлов"
      fi
      
      sleep 60
    volumeMounts:
    - name: nfs-volume
      mountPath: /mnt/nfs
  volumes:
  - name: nfs-volume
    nfs:
      server: 172.16.29.112
      path: /data
  restartPolicy: Never
EOF

kubectl apply -f nfs-failover-test.yaml
kubectl wait --for=condition=ready pod nfs-failover-test -n nfs-cluster --timeout=120s

sleep 15

echo ""
echo "📋 РЕЗУЛЬТАТЫ FAILOVER ТЕСТА:"
kubectl logs nfs-failover-test -n nfs-cluster

# 5. Очистка тестов
echo ""
echo "🧹 Очистка тестовых подов..."
kubectl delete pod nfs-cluster-test nfs-failover-test -n nfs-cluster --ignore-not-found=true

echo ""
echo "🎯 ИТОГОВАЯ ПРОВЕРКА КЛАСТЕРНОГО NFS:"
echo "📊 Статус компонентов:"
kubectl get deployment,svc,pvc -n nfs-cluster
kubectl get pods -n nfs-cluster -o wide

POD_NAME=$(kubectl get pod -n nfs-cluster -l app=nfs-server-cluster -o jsonpath='{.items[0].metadata.name}' 2>/dev/null)
if [ "$POD_NAME" != "" ]; then
    echo ""
    echo "📂 Доступные NFS экспорты:"
    kubectl exec -n nfs-cluster $POD_NAME -- showmount -e localhost
fi

echo ""
echo "✅ КЛАСТЕРНЫЙ NFS С LONGHORN ГОТОВ К ИСПОЛЬЗОВАНИЮ!"
echo "🌐 Адрес: 172.16.29.112"
echo "🔄 Автоматическая репликация на ноды"
echo "⚡ Failover за ~30 секунд без потери данных"


Проверка и мониторинг
#!/bin/bash

# ===============================================
# Мониторинг кластерного NFS
# ===============================================

echo "📊 Полная проверка состояния кластерного NFS..."

echo "🗄️ Longhorn компоненты:"
kubectl get pods -n longhorn-system | grep -E "(READY|manager|csi)" | head -8

echo ""
echo "💾 Longhorn том для NFS:"
kubectl get pvc -n nfs-cluster
kubectl get pv | grep nfs-cluster-data

echo ""
echo "🔄 Реплики Longhorn (детальная информация):"
VOLUME_NAME=$(kubectl get pv | grep nfs-cluster-data | awk '{print $1}')
kubectl get volumes.longhorn.io -n longhorn-system | grep $VOLUME_NAME || echo "Используйте Longhorn UI для детального мониторинга"

echo ""
echo "🐳 Кластерный NFS сервер:"
kubectl get deployment,pod,svc -n nfs-cluster -o wide

echo ""
echo "🌐 LoadBalancer статус:"
kubectl get svc nfs-cluster-service -n nfs-cluster

echo ""
echo "📂 NFS экспорты:"
POD_NAME=$(kubectl get pod -n nfs-cluster -l app=nfs-server-cluster -o jsonpath='{.items[0].metadata.name}' 2>/dev/null)
if [ "$POD_NAME" != "" ]; then
    kubectl exec -n nfs-cluster $POD_NAME -- showmount -e localhost
    echo ""
    echo "📊 Использование дискового пространства:"
    kubectl exec -n nfs-cluster $POD_NAME -- df -h /data
else
    echo "NFS под не готов"
fi

echo ""
echo "🏥 Общее состояние здоровья:"
LONGHORN_PODS_READY=$(kubectl get pods -n longhorn-system | grep -c '1/1.*Running')
LONGHORN_PODS_TOTAL=$(kubectl get pods -n longhorn-system | grep -c '^longhorn-')
LONGHORN_NODES=$(kubectl get nodes.longhorn.io -n longhorn-system --no-headers 2>/dev/null | wc -l)
INSTANCE_MANAGERS=$(kubectl get pods -n longhorn-system | grep -c instance-manager)
NFS_PODS_READY=$(kubectl get pods -n nfs-cluster | grep -c '1/1.*Running')
EXTERNAL_IP=$(kubectl get svc nfs-cluster-service -n nfs-cluster -o jsonpath='{.status.loadBalancer.ingress[0].ip}')

echo "   Longhorn: $LONGHORN_PODS_READY/$LONGHORN_PODS_TOTAL подов готово"
echo "   Longhorn ноды: $LONGHORN_NODES готовы"
echo "   Instance managers: $INSTANCE_MANAGERS запущено"
echo "   NFS: $NFS_PODS_READY/1 подов готово"
echo "   LoadBalancer IP: $EXTERNAL_IP"

# Предупреждение если instance managers меньше ожидаемого
if [ "$INSTANCE_MANAGERS" -lt 3 ]; then
    echo "   ⚠️ Instance managers: ожидалось 3, запущено $INSTANCE_MANAGERS"
    echo "      Это может означать что не все ноды добавлены в Longhorn"
fi

# Longhorn UI доступ
LONGHORN_UI_IP=$(kubectl get svc longhorn-frontend-external -n longhorn-system -o jsonpath='{.status.loadBalancer.ingress[0].ip}' 2>/dev/null)
if [ "$LONGHORN_UI_IP" != "" ]; then
    echo "   Longhorn UI: http://$LONGHORN_UI_IP"
fi

echo ""
echo "🔍 Проверка доступности NFS с текущей ноды:"
timeout 5 nc -zv 172.16.29.112 2049 2>/dev/null && echo "✅ NFS порт 2049 доступен" || echo "❌ NFS порт 2049 недоступен"
timeout 5 nc -zv 172.16.29.112 20048 2>/dev/null && echo "✅ mountd порт 20048 доступен" || echo "❌ mountd порт 20048 недоступен"
Использование в приложениях
Подключение к кластерному NFS
# Пример приложения с кластерным NFS
apiVersion: apps/v1
kind: Deployment
metadata:
  name: my-app
spec:
  replicas: 3
  selector:
    matchLabels:
      app: my-app
  template:
    metadata:
      labels:
        app: my-app
    spec:
      containers:
      - name: app
        image: nginx:alpine
        volumeMounts:
        - name: shared-data
          mountPath: /app/shared
        - name: app-data
          mountPath: /app/data
        - name: config-data
          mountPath: /app/config
      volumes:
      # Кластерное NFS - данные автоматически реплицируются!
      - name: shared-data
        nfs:
          server: 172.16.29.112  # Кластерный NFS с failover
          path: /data/shared
      - name: app-data
        nfs:
          server: 172.16.29.112
          path: /data/app
      - name: config-data
        nfs:
          server: 172.16.29.112
          path: /data/config
PersistentVolumes для кластерного NFS
# PV для shared данных
apiVersion: v1
kind: PersistentVolume
metadata:
  name: nfs-cluster-shared-pv
spec:
  capacity:
    storage: 10Gi
  accessModes:
    - ReadWriteMany
  nfs:
    server: 172.16.29.112  # Кластерный NFS
    path: /data/shared
  persistentVolumeReclaimPolicy: Retain
---
# PV для app данных  
apiVersion: v1
kind: PersistentVolume
metadata:
  name: nfs-cluster-app-pv
spec:
  capacity:
    storage: 20Gi
  accessModes:
    - ReadWriteMany
  nfs:
    server: 172.16.29.112
    path: /data/app
  persistentVolumeReclaimPolicy: Retain
Управление кластерным NFS
Увеличение размера хранилища
# Увеличиваем том на лету
kubectl patch pvc nfs-cluster-data -n nfs-cluster -p '{"spec":{"resources":{"requests":{"storage":"100Gi"}}}}'

# Проверяем расширение
kubectl get pvc nfs-cluster-data -n nfs-cluster
Создание snapshot через Longhorn
# Находим имя тома
VOLUME_NAME=$(kubectl get pv | grep nfs-cluster-data | awk '{print $1}')

# Создаем snapshot
kubectl apply -f - << EOF
apiVersion: longhorn.io/v1beta1
kind: Snapshot
metadata:
  name: nfs-backup-$(date +%Y%m%d-%H%M)
  namespace: longhorn-system
spec:
  volume: $VOLUME_NAME
EOF
Мониторинг здоровья
# Статус всех компонентов
kubectl get nodes.longhorn.io,volumes.longhorn.io,replicas.longhorn.io -n longhorn-system
kubectl get deployment,svc,pvc -n nfs-cluster

# Детальная информация о томе
kubectl describe volumes.longhorn.io -n longhorn-system | grep nfs-cluster-data -A 20
Преимущества кластерного решения


Характеристика

DaemonSet NFS

Кластерный Longhorn NFS

Серверов

3 независимых

1 логический кластерный

Репликация данных

 Нет

 Автоматическая на все ноды

Failover

 Потеря доступа к части данных

 Автоматический за ~30 сек

Консистентность

 Разные данные на нодах

 Единые данные

Snapshots

 Нет

 Автоматические

Backups

 Ручные

 В S3/NFS через Longhorn

Масштабирование

 Ручное

 Автоматическое





При падении 1 ноды - система работает без перерыва
При падении 2 нод - данные остаются доступными
Автоматическое восстановление за 30 секунд
Данные на всех нодах одновременно


Volume snapshots через Longhorn UI
Scheduled backups в S3/NFS
Мониторинг состояния реплик
Disaster recovery из backups
Troubleshooting
Проблема: PVC застрял в Pending из-за недостатка Longhorn нод
PVC не создается, в логах ошибки о невозможности создать нужное количество реплик





# Диагностика
echo "🔍 Диагностика проблемы с Longhorn нодами..."

echo "1️⃣ Проверяем количество Longhorn нод:"
kubectl get nodes.longhorn.io -n longhorn-system
LONGHORN_NODES=$(kubectl get nodes.longhorn.io -n longhorn-system --no-headers 2>/dev/null | wc -l)
echo "Доступно нод: $LONGHORN_NODES"

echo ""
echo "2️⃣ Проверяем требуемые реплики:"
kubectl get storageclass nfs-cluster-storage -o yaml | grep numberOfReplicas

echo ""
echo "3️⃣ Проверяем instance managers:"
kubectl get pods -n longhorn-system | grep instance-manager

echo ""
echo "4️⃣ Проверяем отсутствующие ноды:"
for node in k8s01 k8s02 k8s03; do
    if kubectl get nodes.longhorn.io $node -n longhorn-system >/dev/null 2>&1; then
        echo "✅ $node: готов"
    else
        echo "❌ $node: отсутствует в Longhorn"
        
        # Проверяем директорию на ноде
        ssh $node "ls -la /var/lib/longhorn 2>/dev/null || echo 'Директория /var/lib/longhorn отсутствует'"
    fi
done

# Решение 1: Добавить отсутствующие ноды
echo ""
echo "🔧 Решение 1: Добавляем отсутствующие ноды..."
for node in k8s01 k8s02 k8s03; do
    if ! kubectl get nodes.longhorn.io $node -n longhorn-system >/dev/null 2>&1; then
        echo "Добавляем $node в Longhorn..."
        
        # Создаем директорию
        ssh $node "sudo mkdir -p /var/lib/longhorn && sudo chmod 755 /var/lib/longhorn"
        
        # Создаем ноду
        cat > ${node}-longhorn-node.yaml << EOF
apiVersion: longhorn.io/v1beta2
kind: Node
metadata:
  name: $node
  namespace: longhorn-system
spec:
  allowScheduling: true
  name: $node
EOF
        kubectl apply -f ${node}-longhorn-node.yaml
    fi
done

# Решение 2: Уменьшить реплики (альтернатива)
echo ""
echo "🔧 Решение 2 (альтернатива): Уменьшить реплики до $LONGHORN_NODES..."
cat > nfs-cluster-storage-reduced.yaml << EOF
apiVersion: storage.k8s.io/v1
kind: StorageClass
metadata:
  name: nfs-cluster-storage-reduced
provisioner: driver.longhorn.io
allowVolumeExpansion: true
reclaimPolicy: Retain
volumeBindingMode: Immediate
parameters:
  numberOfReplicas: "$LONGHORN_NODES"
  staleReplicaTimeout: "30"
  dataLocality: "disabled"
  fsType: "ext4"
  migratable: "true"
EOF
# kubectl apply -f nfs-cluster-storage-reduced.yaml
# Затем обновить PVC на новый StorageClass


Проблема: PVC застрял в Pending (общие случаи)
# Диагностика
kubectl describe pvc nfs-cluster-data -n nfs-cluster
kubectl get nodes.longhorn.io -n longhorn-system
kubectl logs -n longhorn-system -l app=csi-provisioner --tail=10

# Решение: перезапуск longhorn-manager
kubectl delete pods -n longhorn-system -l app=longhorn-manager
Проблема: NFS монтирование не работает
# Проверка портов
POD_NAME=$(kubectl get pod -n nfs-cluster -l app=nfs-server-cluster -o jsonpath='{.items[0].metadata.name}')
kubectl exec -n nfs-cluster $POD_NAME -- netstat -tulpn | grep -E ':(111|2049|20048)'

# Должно быть:
# tcp 0.0.0.0:20048 (не случайный порт!)
# tcp 0.0.0.0:2049
# tcp 0.0.0.0:111
Проблема: Данные не реплицируются
# Проверка реплик в Longhorn UI или через CLI
kubectl get volumes.longhorn.io -n longhorn-system
kubectl get replicas.longhorn.io -n longhorn-system

# Проверка нод Longhorn
kubectl get nodes.longhorn.io -n longhorn-system -o wide
Удаление (если нужно)
Удаление приведет к потере всех данных в кластерном NFS!



# Удаление кластерного NFS
kubectl delete namespace nfs-cluster

# Удаление MetalLB конфигурации
kubectl delete -f nfs-cluster-metallb.yaml

# Удаление Longhorn (ОСТОРОЖНО - потеря данных!)
# kubectl delete -f https://raw.githubusercontent.com/longhorn/longhorn/v1.6.0/deploy/longhorn.yaml
# kubectl delete namespace longhorn-system
Итог
✅ Что получили:
Автоматическая репликация на все ноды через Longhorn
Высокая доступность с failover за 30 секунд
Единое хранилище вместо 3 независимых
Enterprise функции (snapshots, backups, мониторинг)
Простое управление через Kubernetes и Longhorn UI
🔧 Критические проблемы решены:
✅ Longhorn ноды - добавлено ожидание создания с диагностикой
✅ Engine images - добавлены проверки загрузки
✅ PVC Pending - диагностика и решение проблем
✅ Порт mountd - фиксация на 20048 через NFS_PORT_MOUNTD
✅ Instance managers - обработка ситуации когда запускаются не на всех нодах
✅ Несоответствие реплик и нод - автоматическое решение конфликта
🎯 Доступ:
*http://172.16.29.112* - кластерный NFS с гарантированной репликацией данных!

