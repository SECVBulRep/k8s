Настройка Redis HA с HAProxy и автоматическими ACL пользователями
Предварительные настройки
MetalLB конфигурация
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
Установка Redis кластера с автоматическими ACL пользователями
# Создаем namespace
kubectl create namespace redis

# Создаем values файл с lifecycle hooks для автоматического создания ACL пользователей
cat > redis-values-lifecycle.yaml << 'EOF'
auth:
  enabled: true
  password: "redis-admin-password"

architecture: replication
replica:
  replicaCount: 2
  # Используем lifecycle postStart hook для создания ACL пользователей после запуска Redis
  lifecycleHooks:
    postStart:
      exec:
        command:
        - /bin/sh
        - -c
        - |
          sleep 15
          echo "Создаем ACL пользователей на replica после запуска..."
          redis-cli -h localhost -p 6379 -a redis-admin-password ACL SETUSER admin-user on '>admin-secure-password' '~*' '&*' '+@all' || true
          redis-cli -h localhost -p 6379 -a redis-admin-password ACL SETUSER haproxy-user on '>haproxy-check-password' '~*' '+ping' '+info' || true
          echo "ACL пользователи созданы на replica!"

master:
  # Используем lifecycle postStart hook для создания ACL пользователей после запуска Redis
  lifecycleHooks:
    postStart:
      exec:
        command:
        - /bin/sh
        - -c
        - |
          sleep 15
          echo "Создаем ACL пользователей на master после запуска..."
          redis-cli -h localhost -p 6379 -a redis-admin-password ACL SETUSER admin-user on '>admin-secure-password' '~*' '&*' '+@all' || true
          redis-cli -h localhost -p 6379 -a redis-admin-password ACL SETUSER haproxy-user on '>haproxy-check-password' '~*' '+ping' '+info' || true
          echo "ACL пользователи созданы на master!"

sentinel:
  enabled: true
EOF

# Установка Redis с автоматическими ACL пользователями через lifecycle hooks
helm install redis oci://registry-1.docker.io/bitnamicharts/redis -f redis-values-lifecycle.yaml -n redis

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

# Проверяем что ACL пользователи автоматически созданы на ВСЕХ нодах
for node in redis-node-0 redis-node-1 redis-node-2; do
  echo "=== ACL пользователи на $node ==="
  kubectl exec -n redis $node -c redis -- redis-cli -a redis-admin-password ACL LIST | grep -E "admin-user|haproxy-user"
done

# Запоминаем какая нода является master (обычно redis-node-0)
Создание HAProxy с аутентификацией
✅ ACL пользователи уже созданы автоматически на всех нодах через lifecycle hooks!

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
Проверка и тестирование
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
redis-cli -h 172.16.29.110 --user admin-user --pass admin-secure-password SET test:key "Redis + HAProxy + Auto ACL works!"

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
echo "- Автоматическое создание пользователей на всех нодах при запуске"
Автоматическое восстановление после failover
✅ ПОЛНОСТЬЮ АВТОМАТИЧЕСКОЕ: ACL пользователи автоматически присутствуют на всех нодах благодаря lifecycle hooks!

Тестирование автоматического failover:
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

# 6. Проверяем что пользователи автоматически присутствуют на новом master
NEW_MASTER="redis-node-1"  # Замените на актуальный master
kubectl exec -n redis $NEW_MASTER -c redis -- redis-cli -a redis-admin-password ACL LIST

# 7. Проверяем что HAProxy автоматически переключился (ждем 1-2 минуты)
kubectl logs -n redis -l app=haproxy --tail=10 | grep "is UP"

# 8. Тестируем что система работает автоматически
redis-cli -h 172.16.29.110 --user admin-user --pass admin-secure-password ping
redis-cli -h 172.16.29.110 --user admin-user --pass admin-secure-password SET failover:test "Auto failover works!"
redis-cli -h 172.16.29.110 --user admin-user --pass admin-secure-password GET failover:test


## Пароли в настройке:

- **Redis admin password**: `redis-admin-password` (встроенный пользователь default)
- **Администратор**: `admin-user` / `admin-secure-password` (создается автоматически через lifecycle hooks)
- **HAProxy пользователь**: `haproxy-user` / `haproxy-check-password` (создается автоматически через lifecycle hooks)

**⚠️ ВАЖНО:** В продакшене обязательно измените все пароли на более сложные!


# УДАЛЕНИЕ ВСЕГО ЧТО СОЗДАЛИ !!!
kubectl delete -f haproxy-all.yaml

# Удаляем Redis через Helm  
helm uninstall redis -n redis

# Удаляем namespace
kubectl delete namespace redis

# Проверяем что все удалено
kubectl get all -n redis 2>/dev/null || echo "Namespace удален"