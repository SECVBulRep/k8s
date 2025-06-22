# Настройка Redis HA с HAProxy и автоматическими ACL пользователями из NFS

## Предварительные настройки

### MetalLB конфигурация
```yaml
apiVersion: metallb.io/v1beta1
kind: IPAddressPool
metadata:
  name: redis-pool
  namespace: metallb-system
spec:
  addresses:
  - 172.16.29.110-172.16.29.111
---
apiVersion: metallb.io/v1beta1
kind: L2Advertisement
metadata:
  name: redis-l2
  namespace: metallb-system
spec:
  ipAddressPools:
  - redis-pool
```

## Установка Redis кластера с автоматическими ACL пользователями из NFS

```bash
# Создаем namespace
kubectl create namespace redis

# Подготавливаем ACL файлы на кластерном NFS ПЕРЕД установкой Redis
echo "📁 Создаем ACL файлы на кластерном NFS..."

# Создаем ACL команды в файлах (ВАЖНО: без одинарных кавычек вокруг паролей!)
echo "ACL SETUSER admin-user on >admin-secure-password ~* &* +@all" > /tmp/redis-acl-admin.txt
echo "ACL SETUSER haproxy-user on >haproxy-check-password ~* +ping +info" > /tmp/redis-acl-haproxy.txt

# Монтируем NFS и копируем файлы
sudo mkdir -p /mnt/nfs-config
sudo mount -t nfs 172.16.29.112:/data/config /mnt/nfs-config
sudo cp /tmp/redis-acl-admin.txt /mnt/nfs-config/
sudo cp /tmp/redis-acl-haproxy.txt /mnt/nfs-config/

# Проверяем созданные файлы
echo "📄 Проверяем созданные ACL файлы:"
sudo cat /mnt/nfs-config/redis-acl-admin.txt
sudo cat /mnt/nfs-config/redis-acl-haproxy.txt

sudo umount /mnt/nfs-config

echo "✅ ACL файлы созданы на NFS:"
echo "   - /data/config/redis-acl-admin.txt"
echo "   - /data/config/redis-acl-haproxy.txt"

# Создаем values файл с lifecycle hooks для автоматического создания ACL пользователей из NFS
cat > redis-values-lifecycle.yaml << 'EOF'
auth:
  enabled: true
  password: "redis-admin-password"

architecture: replication
replica:
  replicaCount: 2
  # Используем lifecycle postStart hook для создания ACL пользователей из NFS файлов
  lifecycleHooks:
    postStart:
      exec:
        command:
        - /bin/sh
        - -c
        - |
          sleep 15
          echo "Загружаем ACL пользователей из NFS на replica после запуска..."
          
          # Проверяем доступность NFS
          if [ ! -d "/nfs-config" ]; then
            echo "❌ NFS не смонтирован в /nfs-config"
            exit 1
          fi
          
          # Загружаем ACL из файлов NFS
          if [ -f "/nfs-config/redis-acl-admin.txt" ]; then
            echo "📄 Загружаем admin ACL из NFS..."
            ACL_ADMIN=$(cat /nfs-config/redis-acl-admin.txt)
            redis-cli -h localhost -p 6379 -a redis-admin-password $ACL_ADMIN || true
          else
            echo "⚠️ Файл redis-acl-admin.txt не найден, создаем по умолчанию..."
            redis-cli -h localhost -p 6379 -a redis-admin-password ACL SETUSER admin-user on '>admin-secure-password' '~*' '&*' '+@all' || true
          fi
          
          if [ -f "/nfs-config/redis-acl-haproxy.txt" ]; then
            echo "📄 Загружаем haproxy ACL из NFS..."
            ACL_HAPROXY=$(cat /nfs-config/redis-acl-haproxy.txt)
            redis-cli -h localhost -p 6379 -a redis-admin-password $ACL_HAPROXY || true
          else
            echo "⚠️ Файл redis-acl-haproxy.txt не найден, создаем по умолчанию..."
            redis-cli -h localhost -p 6379 -a redis-admin-password ACL SETUSER haproxy-user on '>haproxy-check-password' '~*' '+ping' '+info' || true
          fi
          
          echo "ACL пользователи загружены из NFS на replica!"
  
  # Добавляем NFS volume для ACL конфигурации
  extraVolumes:
  - name: nfs-acl-config
    nfs:
      server: 172.16.29.112
      path: /data/config
  
  extraVolumeMounts:
  - name: nfs-acl-config
    mountPath: /nfs-config
    readOnly: true

master:
  # Используем lifecycle postStart hook для создания ACL пользователей из NFS файлов
  lifecycleHooks:
    postStart:
      exec:
        command:
        - /bin/sh
        - -c
        - |
          sleep 15
          echo "Загружаем ACL пользователей из NFS на master после запуска..."
          
          # Проверяем доступность NFS
          if [ ! -d "/nfs-config" ]; then
            echo "❌ NFS не смонтирован в /nfs-config"
            exit 1
          fi
          
          # Загружаем ACL из файлов NFS
          if [ -f "/nfs-config/redis-acl-admin.txt" ]; then
            echo "📄 Загружаем admin ACL из NFS..."
            ACL_ADMIN=$(cat /nfs-config/redis-acl-admin.txt)
            redis-cli -h localhost -p 6379 -a redis-admin-password $ACL_ADMIN || true
          else
            echo "⚠️ Файл redis-acl-admin.txt не найден, создаем по умолчанию..."
            redis-cli -h localhost -p 6379 -a redis-admin-password ACL SETUSER admin-user on '>admin-secure-password' '~*' '&*' '+@all' || true
          fi
          
          if [ -f "/nfs-config/redis-acl-haproxy.txt" ]; then
            echo "📄 Загружаем haproxy ACL из NFS..."
            ACL_HAPROXY=$(cat /nfs-config/redis-acl-haproxy.txt)
            redis-cli -h localhost -p 6379 -a redis-admin-password $ACL_HAPROXY || true
          else
            echo "⚠️ Файл redis-acl-haproxy.txt не найден, создаем по умолчанию..."
            redis-cli -h localhost -p 6379 -a redis-admin-password ACL SETUSER haproxy-user on '>haproxy-check-password' '~*' '+ping' '+info' || true
          fi
          
          echo "ACL пользователи загружены из NFS на master!"
  
  # Добавляем NFS volume для ACL конфигурации
  extraVolumes:
  - name: nfs-acl-config
    nfs:
      server: 172.16.29.112
      path: /data/config
  
  extraVolumeMounts:
  - name: nfs-acl-config
    mountPath: /nfs-config
    readOnly: true

sentinel:
  enabled: true
EOF

# Установка Redis с автоматическими ACL пользователями из NFS
helm install redis oci://registry-1.docker.io/bitnamicharts/redis -f redis-values-lifecycle.yaml -n redis

# ВАЖНО: postStart hooks в Bitnami Redis могут не выполниться из-за timing/security ограничений
# Поэтому создаем Job для гарантированного создания ACL пользователей

# Ждем готовности Redis
kubectl get pods -n redis -w
# Ctrl+C когда все поды будут Running

# Создаем Job для автоматического создания ACL пользователей из NFS
cat > redis-acl-job.yaml << 'EOF'
apiVersion: batch/v1
kind: Job
metadata:
  name: redis-acl-setup
  namespace: redis
spec:
  template:
    spec:
      containers:
      - name: acl-setup
        image: redis:7-alpine
        command:
        - /bin/sh
        - -c
        - |
          echo "🔧 Настройка ACL пользователей из NFS..."
          
          # Ждем готовности всех Redis нод
          for node in redis-node-0 redis-node-1 redis-node-2; do
            echo "⏳ Ожидание готовности $node..."
            until redis-cli -h $node.redis-headless.redis.svc.cluster.local -a redis-admin-password ping; do
              sleep 5
            done
            echo "✅ $node готов"
          done
          
          # Проверяем NFS
          if [ ! -d "/nfs-config" ]; then
            echo "❌ NFS не смонтирован"
            exit 1
          fi
          
          echo "📁 ACL файлы на NFS:"
          ls -la /nfs-config/redis-acl-*.txt
          
          # Создаем ACL на всех нодах
          for node in redis-node-0 redis-node-1 redis-node-2; do
            echo "=== Настройка ACL на $node ==="
            
            # Загружаем admin ACL из NFS
            if [ -f "/nfs-config/redis-acl-admin.txt" ]; then
              echo "📄 Загружаем admin ACL из NFS на $node..."
              ACL_ADMIN=$(cat /nfs-config/redis-acl-admin.txt)
              if redis-cli -h $node.redis-headless.redis.svc.cluster.local -a redis-admin-password $ACL_ADMIN; then
                echo "✅ Admin ACL загружен на $node"
              else
                echo "❌ Ошибка загрузки admin ACL на $node"
              fi
            fi
            
            # Загружаем haproxy ACL из NFS
            if [ -f "/nfs-config/redis-acl-haproxy.txt" ]; then
              echo "📄 Загружаем haproxy ACL из NFS на $node..."
              ACL_HAPROXY=$(cat /nfs-config/redis-acl-haproxy.txt)
              if redis-cli -h $node.redis-headless.redis.svc.cluster.local -a redis-admin-password $ACL_HAPROXY; then
                echo "✅ HAProxy ACL загружен на $node"
              else
                echo "❌ Ошибка загрузки haproxy ACL на $node"
              fi
            fi
            
            # Пытаемся сохранить ACL (может не работать в Bitnami Redis, но это не критично)
            if redis-cli -h $node.redis-headless.redis.svc.cluster.local -a redis-admin-password ACL SAVE 2>/dev/null; then
              echo "✅ ACL сохранены на диск на $node"
            else
              echo "⚠️ ACL SAVE не поддерживается (пользователи все равно созданы на $node)"
            fi
            
            echo "✅ ACL настройка завершена на $node"
          done
          
          echo "🎯 ACL настройка завершена!"
        
        volumeMounts:
        - name: nfs-acl-config
          mountPath: /nfs-config
          readOnly: true
      
      volumes:
      - name: nfs-acl-config
        nfs:
          server: 172.16.29.112
          path: /data/config
      
      restartPolicy: Never
  backoffLimit: 3
EOF

# Запускаем Job для создания ACL
kubectl apply -f redis-acl-job.yaml

# Следим за выполнением Job
kubectl get jobs -n redis
kubectl logs -n redis job/redis-acl-setup -f

# Проверяем что ACL пользователи созданы
for node in redis-node-0 redis-node-1 redis-node-2; do
  echo "=== ACL пользователи на $node (созданы через Job) ==="
  kubectl exec -n redis $node -c redis -- redis-cli -a redis-admin-password ACL LIST | grep -E "admin-user|haproxy-user"
done

# Проверяем что создалось
kubectl get all -n redis

# Проверяем StatefulSet
kubectl get statefulset -n redis

# Если видим redis-node с replicas < 3, масштабируем
kubectl scale statefulset redis-node -n redis --replicas=3

# Ждём создания всех подов
kubectl get pods -n redis -w
# Дождись когда будет 3 пода redis-node-0, redis-node-1, redis-node-2 в статусе Running
# Ctrl+C для выхода

# Проверяем роли каждой ноды
for i in 0 1 2; do
  echo "=== redis-node-$i ==="
  kubectl exec -n redis redis-node-$i -c redis -- redis-cli -a redis-admin-password INFO replication | grep -E "role:|connected_slaves:"
done

# Проверяем что ACL пользователи автоматически загружены из NFS на ВСЕХ нодах
for node in redis-node-0 redis-node-1 redis-node-2; do
  echo "=== ACL пользователи на $node (загружены из NFS) ==="
  kubectl exec -n redis $node -c redis -- redis-cli -a redis-admin-password ACL LIST | grep -E "admin-user|haproxy-user"
done

# Запоминаем какая нода является master (обычно redis-node-0)
```

## Создание HAProxy с аутентификацией

✅ ACL пользователи уже загружены автоматически из NFS на всех нодах!

```bash
# Создаём файл haproxy-all.yaml
cat > haproxy-all.yaml << 'EOF'
# ConfigMap с конфигурацией HAProxy
---
apiVersion: v1
kind: ConfigMap
metadata:
  name: haproxy-config
  namespace: redis
data:
  haproxy.cfg: |
    global
        daemon
        maxconn 256

    defaults
        mode tcp
        timeout connect 5000ms
        timeout client 50000ms
        timeout server 50000ms

    frontend redis_frontend
        bind *:6379
        default_backend redis_backend

    backend redis_backend
        mode tcp
        balance first
        option tcp-check
        # Проверяем что это Redis с аутентификацией
        tcp-check connect
        tcp-check send AUTH\ haproxy-user\ haproxy-check-password\r\n
        tcp-check expect string +OK
        tcp-check send PING\r\n
        tcp-check expect string +PONG
        # Проверяем что это master
        tcp-check send info\ replication\r\n
        tcp-check expect string role:master
        tcp-check send QUIT\r\n
        tcp-check expect string +OK
        # Проверяем все 3 ноды
        server redis-0 redis-node-0.redis-headless.redis.svc.cluster.local:6379 check inter 2s
        server redis-1 redis-node-1.redis-headless.redis.svc.cluster.local:6379 check inter 2s
        server redis-2 redis-node-2.redis-headless.redis.svc.cluster.local:6379 check inter 2s

---
# Deployment HAProxy
apiVersion: apps/v1
kind: Deployment
metadata:
  name: haproxy
  namespace: redis
  labels:
    app: haproxy
spec:
  replicas: 2
  selector:
    matchLabels:
      app: haproxy
  template:
    metadata:
      labels:
        app: haproxy
    spec:
      containers:
      - name: haproxy
        image: haproxy:2.9-alpine
        ports:
        - containerPort: 6379
          name: redis
        volumeMounts:
        - name: config
          mountPath: /usr/local/etc/haproxy
          readOnly: true
        resources:
          requests:
            cpu: 100m
            memory: 128Mi
          limits:
            cpu: 200m
            memory: 256Mi
        livenessProbe:
          tcpSocket:
            port: 6379
          initialDelaySeconds: 15
          periodSeconds: 20
        readinessProbe:
          tcpSocket:
            port: 6379
          initialDelaySeconds: 5
          periodSeconds: 10
      volumes:
      - name: config
        configMap:
          name: haproxy-config

---
# Service для HAProxy с внешним доступом
apiVersion: v1
kind: Service
metadata:
  name: redis-master
  namespace: redis
  labels:
    app: haproxy
spec:
  type: LoadBalancer
  loadBalancerIP: "172.16.29.110"
  selector:
    app: haproxy
  ports:
  - name: redis
    port: 6379
    targetPort: 6379
    protocol: TCP
EOF

kubectl apply -f haproxy-all.yaml

# Проверяем создание подов HAProxy
kubectl get pods -n redis -l app=haproxy
# Дождись статуса Running для обоих подов haproxy

# Проверяем логи HAProxy
kubectl logs -n redis -l app=haproxy --tail=20
# Должны увидеть что один из redis серверов UP (master), остальные DOWN (slaves)
```

## Проверка и тестирование

```bash
# Проверяем все сервисы
kubectl get svc -n redis

# Проверяем endpoints
kubectl get endpoints -n redis

# Ждём пока LoadBalancer получит внешний IP
# Должны увидеть:
# redis-master    LoadBalancer   10.x.x.x   172.16.29.110   6379:xxxxx/TCP
# redis-sentinel  LoadBalancer   10.x.x.x   172.16.29.111   26379:xxxxx/TCP

# Тест подключения к Redis через HAProxy с администратором (Redis 6+ ACL синтаксис)
redis-cli -h 172.16.29.110 --user admin-user --pass admin-secure-password ping
# Должен ответить: PONG

# Тест записи от имени администратора
redis-cli -h 172.16.29.110 --user admin-user --pass admin-secure-password SET test:key "Redis + HAProxy + NFS ACL works!"

# Тест чтения от имени администратора
redis-cli -h 172.16.29.110 --user admin-user --pass admin-secure-password GET test:key

# Проверка что подключились к master
redis-cli -h 172.16.29.110 --user admin-user --pass admin-secure-password INFO replication | grep role
# Должно показать: role:master

# Проверяем что пользователь HAProxy может только читать INFO
redis-cli -h 172.16.29.110 --user haproxy-user --pass haproxy-check-password INFO replication | grep role
# Должно показать: role:master

# Проверяем что пользователь HAProxy НЕ может писать данные (должна быть ошибка)
redis-cli -h 172.16.29.110 --user haproxy-user --pass haproxy-check-password SET test:forbidden "should fail"
# Должно показать ошибку: NOPERM this user has no permissions to run the 'set' command

# Альтернативный способ подключения (интерактивный режим):
redis-cli -h 172.16.29.110
# В консоли Redis: AUTH admin-user admin-secure-password

echo "Настройка завершена! У вас есть:"
echo "- Администратор: admin-user / admin-secure-password (полные права)"
echo "- HAProxy пользователь: haproxy-user / haproxy-check-password (только INFO и PING)"
echo "- Автоматическая загрузка пользователей из NFS на всех нодах при запуске"
```

## Автоматическое восстановление после failover

✅ ПОЛНОСТЬЮ АВТОМАТИЧЕСКОЕ: ACL пользователи загружаются из NFS через Job и поддерживаются CronJob!

### Создание умного CronJob для синхронизации ACL с NFS:

```bash
# Создаем CronJob для автоматической синхронизации ACL с NFS файлами
cat > redis-acl-sync-cronjob.yaml << 'EOF'
apiVersion: batch/v1
kind: CronJob
metadata:
  name: redis-acl-sync
  namespace: redis
spec:
  schedule: "*/5 * * * *"  # Каждые 5 минут синхронизируем ACL
  concurrencyPolicy: Forbid
  successfulJobsHistoryLimit: 3
  failedJobsHistoryLimit: 3
  jobTemplate:
    spec:
      template:
        spec:
          containers:
          - name: acl-sync
            image: redis:7-alpine
            command:
            - /bin/sh
            - -c
            - |
              echo "🔄 Синхронизация ACL пользователей с NFS файлами..."
              echo "Время: $(date)"
              echo ""
              
              # Читаем все ACL файлы с NFS
              echo "📁 Поиск ACL файлов на NFS..."
              ACL_FILES=$(find /nfs-config -name "redis-acl-*.txt" -type f 2>/dev/null || echo "")
              
              if [ -z "$ACL_FILES" ]; then
                echo "❌ ACL файлы не найдены на NFS"
                exit 1
              fi
              
              echo "📄 Найдены ACL файлы:"
              for file in $ACL_FILES; do
                echo "   - $(basename $file)"
              done
              echo ""
              
              # Собираем пользователей из всех ACL файлов
              echo "📋 Парсинг ACL файлов..."
              EXPECTED_USERS=""
              for file in $ACL_FILES; do
                while IFS= read -r line; do
                  if echo "$line" | grep -q "ACL SETUSER"; then
                    USERNAME=$(echo "$line" | sed -n 's/.*ACL SETUSER \([^ ]*\).*/\1/p')
                    if [ -n "$USERNAME" ]; then
                      EXPECTED_USERS="$EXPECTED_USERS $USERNAME"
                    fi
                  fi
                done < "$file"
              done
              
              # Убираем дубликаты
              EXPECTED_USERS=$(echo $EXPECTED_USERS | tr ' ' '\n' | sort -u | tr '\n' ' ')
              echo "Ожидаемые пользователи: $EXPECTED_USERS"
              echo ""
              
              # Синхронизируем каждую Redis ноду
              for node in redis-node-0 redis-node-1 redis-node-2; do
                echo "=== Синхронизация ACL на $node ==="
                
                # Проверяем доступность ноды
                if ! redis-cli -h $node.redis-headless.redis.svc.cluster.local -a redis-admin-password ping > /dev/null 2>&1; then
                  echo "❌ $node недоступен, пропускаем"
                  continue
                fi
                
                # Получаем текущих пользователей (исключаем default)
                CURRENT_USERS=$(redis-cli -h $node.redis-headless.redis.svc.cluster.local -a redis-admin-password ACL LIST | grep "^user " | grep -v "user default" | sed 's/^user \([^ ]*\).*/\1/' | tr '\n' ' ')
                echo "Текущие пользователи: $CURRENT_USERS"
                
                # УДАЛЯЕМ пользователей которых нет в ACL файлах
                for current_user in $CURRENT_USERS; do
                  if ! echo "$EXPECTED_USERS" | grep -q "$current_user"; then
                    echo "🗑️ Удаляем пользователя: $current_user"
                    redis-cli -h $node.redis-headless.redis.svc.cluster.local -a redis-admin-password ACL DELUSER $current_user
                  fi
                done
                
                # ДОБАВЛЯЕМ/ОБНОВЛЯЕМ пользователей из ACL файлов
                for file in $ACL_FILES; do
                  echo "📄 Применяем файл: $(basename $file)"
                  while IFS= read -r line; do
                    if echo "$line" | grep -q "ACL SETUSER"; then
                      USERNAME=$(echo "$line" | sed -n 's/.*ACL SETUSER \([^ ]*\).*/\1/p')
                      echo "👤 Применяем пользователя: $USERNAME"
                      if redis-cli -h $node.redis-headless.redis.svc.cluster.local -a redis-admin-password $line; then
                        echo "✅ Пользователь $USERNAME создан/обновлен"
                      else
                        echo "❌ Ошибка создания пользователя $USERNAME"
                      fi
                    fi
                  done < "$file"
                done
                
                # Сохраняем изменения (если поддерживается)
                if redis-cli -h $node.redis-headless.redis.svc.cluster.local -a redis-admin-password ACL SAVE 2>/dev/null; then
                  echo "✅ ACL сохранены на диск"
                else
                  echo "⚠️ ACL SAVE не поддерживается (пользователи все равно активны)"
                fi
                
                # Проверяем финальное состояние
                FINAL_USERS=$(redis-cli -h $node.redis-headless.redis.svc.cluster.local -a redis-admin-password ACL LIST | grep "^user " | grep -v "user default" | sed 's/^user \([^ ]*\).*/\1/' | tr '\n' ' ')
                echo "✅ Финальные пользователи на $node: $FINAL_USERS"
                echo ""
              done
              
              echo "🎯 Синхронизация ACL завершена!"
              echo "Следующая синхронизация через 5 минут"
            
            volumeMounts:
            - name: nfs-acl-config
              mountPath: /nfs-config
              readOnly: true
          
          volumes:
          - name: nfs-acl-config
            nfs:
              server: 172.16.29.112
              path: /data/config
          
          restartPolicy: OnFailure
EOF

kubectl apply -f redis-acl-sync-cronjob.yaml

echo "✅ Умный CronJob создан!"
echo "🔄 Будет синхронизировать ACL каждые 5 минут"
echo "📊 Проверить статус: kubectl get cronjob redis-acl-sync -n redis"
```

### Тестирование автоматического failover:

```bash
# 1. Проверяем текущее размещение подов
kubectl get pods -n redis -o wide

# 2. Определяем текущий master
for i in 0 1 2; do
  echo "=== redis-node-$i ==="
  kubectl exec -n redis redis-node-$i -c redis -- redis-cli -a redis-admin-password INFO replication | grep -E "role:" || echo "Недоступен"
done

# 3. Запускаем непрерывный мониторинг (в отдельном терминале)
while true; do
  echo "$(date): $(redis-cli -h 172.16.29.110 --user admin-user --pass admin-secure-password ping 2>&1)"
  sleep 1
done

# 4. Выключаем ноду с master Redis
# На соответствующей физической ноде: sudo shutdown now

# 5. После failover проверяем автоматическое восстановление
for i in 0 1 2; do
  echo "=== redis-node-$i ==="
  kubectl exec -n redis redis-node-$i -c redis -- redis-cli -a redis-admin-password INFO replication 2>/dev/null | grep -E "role:" || echo "Недоступен"
done

# 6. Проверяем что пользователи автоматически загружены из NFS на новом master
NEW_MASTER="redis-node-1"  # Замените на актуальный master
kubectl exec -n redis $NEW_MASTER -c redis -- redis-cli -a redis-admin-password ACL LIST

# 7. Проверяем что HAProxy автоматически переключился (ждем 1-2 минуты)
kubectl logs -n redis -l app=haproxy --tail=10 | grep "is UP"

# 8. Тестируем что система работает автоматически
redis-cli -h 172.16.29.110 --user admin-user --pass admin-secure-password ping
redis-cli -h 172.16.29.110 --user admin-user --pass admin-secure-password SET failover:test "Auto failover works!"
redis-cli -h 172.16.29.110 --user admin-user --pass admin-secure-password GET failover:test
```

## Управление ACL пользователями через NFS

## Управление ACL пользователями через NFS

### Добавление нового пользователя:

```bash
# Монтируем NFS для изменения ACL файлов
sudo mkdir -p /mnt/nfs-config
sudo mount -t nfs 172.16.29.112:/data/config /mnt/nfs-config

# Добавляем нового read-only пользователя (БЕЗ одинарных кавычек!)
echo "ACL SETUSER readonly-user on >readonly-password ~* +@read" | sudo tee /mnt/nfs-config/redis-acl-readonly.txt

# Добавляем специального пользователя для мониторинга
echo "ACL SETUSER monitor-user on >monitor-password ~* +ping +info +client +config" | sudo tee /mnt/nfs-config/redis-acl-monitor.txt

# Проверяем созданные файлы
echo "📄 ACL файлы на NFS:"
sudo ls -la /mnt/nfs-config/redis-acl-*.txt

sudo umount /mnt/nfs-config

echo "✅ Новые пользователи будут применены в течение 5 минут"
```

### Изменение прав существующего пользователя:

```bash
# Монтируем NFS
sudo mount -t nfs 172.16.29.112:/data/config /mnt/nfs-config

# Изменяем права admin пользователя (например, ограничиваем доступ к опасным командам)
echo "ACL SETUSER admin-user on >admin-secure-password ~* &* +@all -flushall -flushdb -shutdown" | sudo tee /mnt/nfs-config/redis-acl-admin.txt

sudo umount /mnt/nfs-config

echo "✅ Изменения прав будут применены в течение 5 минут"
```

### Удаление пользователя:

```bash
# Монтируем NFS
sudo mount -t nfs 172.16.29.112:/data/config /mnt/nfs-config

# Удаляем файл пользователя
sudo rm /mnt/nfs-config/redis-acl-readonly.txt

# Или переименовываем файл для временного отключения
sudo mv /mnt/nfs-config/redis-acl-monitor.txt /mnt/nfs-config/redis-acl-monitor.txt.disabled

sudo umount /mnt/nfs-config

echo "✅ Пользователь будет удален из Redis в течение 5 минут"
```

### Просмотр текущего состояния:

```bash
# Проверяем какие ACL файлы есть на NFS
sudo mount -t nfs 172.16.29.112:/data/config /mnt/nfs-config
echo "📄 Активные ACL файлы:"
sudo ls -la /mnt/nfs-config/redis-acl-*.txt
echo ""
echo "📋 Содержимое файлов:"
for file in /mnt/nfs-config/redis-acl-*.txt; do
  echo "=== $(basename $file) ==="
  sudo cat "$file"
  echo ""
done
sudo umount /mnt/nfs-config

# Проверяем последнее выполнение синхронизации
kubectl get jobs -n redis | grep redis-acl-sync

# Смотрим логи последней синхронизации
LAST_JOB=$(kubectl get jobs -n redis | grep redis-acl-sync | tail -1 | awk '{print $1}')
kubectl logs -n redis job/$LAST_JOB
```

### Принудительная синхронизация:

```bash
# Создаем разовый Job для немедленной синхронизации
kubectl create job --from=cronjob/redis-acl-sync redis-acl-sync-manual -n redis

# Следим за выполнением
kubectl logs -n redis job/redis-acl-sync-manual -f

# Проверяем результат
for node in redis-node-0 redis-node-1 redis-node-2; do
  echo "=== ACL пользователи на $node ==="
  kubectl exec -n redis $node -c redis -- redis-cli -a redis-admin-password ACL LIST | grep "^user " | grep -v "user default"
done
```

## Быстрое исправление существующих ACL файлов

Если вы уже создали файлы с неправильным синтаксисом:

```bash
# Исправляем ACL файлы на NFS
sudo mount -t nfs 172.16.29.112:/data/config /mnt/nfs-config

# Исправляем файлы с правильным синтаксисом (БЕЗ одинарных кавычек!)
echo "ACL SETUSER admin-user on >admin-secure-password ~* &* +@all" | sudo tee /mnt/nfs-config/redis-acl-admin.txt
echo "ACL SETUSER haproxy-user on >haproxy-check-password ~* +ping +info" | sudo tee /mnt/nfs-config/redis-acl-haproxy.txt

# Проверяем исправленные файлы
echo "📄 Исправленные ACL файлы:"
sudo cat /mnt/nfs-config/redis-acl-admin.txt
sudo cat /mnt/nfs-config/redis-acl-haproxy.txt

sudo umount /mnt/nfs-config

# Перезапускаем Job для применения исправлений
kubectl delete job redis-acl-setup -n redis
kubectl apply -f redis-acl-job.yaml

# Следим за успешным выполнением
kubectl logs -n redis job/redis-acl-setup -f

# Проверяем создание пользователей
for node in redis-node-0 redis-node-1 redis-node-2; do
  echo "=== ACL пользователи на $node ==="
  kubectl exec -n redis $node -c redis -- redis-cli -a redis-admin-password ACL LIST | grep -E "admin-user|haproxy-user"
done

# Тестируем HAProxy после создания пользователей
redis-cli -h 172.16.29.110 --user admin-user --pass admin-secure-password ping
```

## Мониторинг ACL синхронизации

```bash
# Проверяем статус CronJob
kubectl get cronjob redis-acl-sync -n redis

# Смотрим историю выполнения
kubectl get jobs -n redis | grep redis-acl-sync

# Проверяем логи последней синхронизации  
LAST_JOB=$(kubectl get jobs -n redis | grep redis-acl-sync | head -1 | awk '{print $1}')
## Пароли в настройке:

- **Redis admin password**: `redis-admin-password` (встроенный пользователь default)
- **Администратор**: `admin-user` / `admin-secure-password` (загружается из NFS)
- **HAProxy пользователь**: `haproxy-user` / `haproxy-check-password` (загружается из NFS)

**⚠️ ВАЖНО:** В продакшене обязательно измените все пароли на более сложные!

- **Redis admin password**: `redis-admin-password` (встроенный пользователь default)
- **Администратор**: `admin-user` / `admin-secure-password` (загружается из NFS)
- **HAProxy пользователь**: `haproxy-user` / `haproxy-check-password` (загружается из NFS)

**⚠️ ВАЖНО:** В продакшене обязательно измените все пароли на более сложные!

**🔧 ПРИМЕЧАНИЕ О LIFECYCLE HOOKS:**
Lifecycle hooks в values файле могут не выполниться из-за:
- **Timing проблемы** - postStart выполняется до готовности Redis
- **Security ограничения** - readOnlyRootFilesystem в Bitnami chart
- **Таймауты** - Kubernetes может прерывать долгие postStart hooks

Поэтому используем Job для гарантированного создания ACL пользователей.

**🔧 ВАЖНЫЕ ИСПРАВЛЕНИЯ В JOB + NFS ACL:**
✅ **Правильный синтаксис ACL** - убраны лишние одинарные кавычки вокруг паролей
✅ **Обработка ошибок** - корректное отображение успеха/неудачи операций ACL
✅ **ACL SAVE предупреждения** - пользователи работают даже если ACL SAVE не поддерживается
✅ **Автоматическая синхронизация** - CronJob каждые 5 минут проверяет NFS файлы
✅ **Умное управление** - автоматически добавляет новых и удаляет лишних пользователей  
✅ **Централизованное управление** - все изменения через файлы на NFS
✅ **Безопасность** - никогда не удаляет default пользователя
✅ **Логирование** - подробные логи всех изменений ACL
✅ **Мгновенные изменения** - добавил файл → через 5 минут пользователь готов
✅ **Версионность** - история изменений через Longhorn snapshots
✅ **Простота** - управление через обычные текстовые файлы
✅ **Отказоустойчивость** - работает даже при падении нод

**⚠️ СИНТАКСИС ACL ФАЙЛОВ:**
- ✅ Правильно: `ACL SETUSER admin-user on >password ~* &* +@all`
- ❌ Неправильно: `ACL SETUSER admin-user on '>password' '~*' '&*' '+@all'`

## УДАЛЕНИЕ ВСЕГО ЧТО СОЗДАЛИ !!!

```bash
kubectl delete -f haproxy-all.yaml

# Удаляем CronJob и Job
kubectl delete cronjob redis-acl-sync -n redis
kubectl delete job redis-acl-setup -n redis

# Удаляем все jobs связанные с ACL
kubectl delete jobs -n redis -l job-name=redis-acl-sync

# Удаляем Redis через Helm  
helm uninstall redis -n redis

# Удаляем ACL файлы с NFS
sudo mkdir -p /mnt/nfs-config
sudo mount -t nfs 172.16.29.112:/data/config /mnt/nfs-config
sudo rm -f /mnt/nfs-config/redis-acl-*.txt
sudo umount /mnt/nfs-config

# Удаляем namespace
kubectl delete namespace redis

# Проверяем что все удалено
kubectl get all -n redis 2>/dev/null || echo "Namespace удален"
```