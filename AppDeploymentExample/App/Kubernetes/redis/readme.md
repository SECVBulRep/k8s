Шаг 1: Установка и настройка MetalLB

Вместо создания собственного metallb-install.yaml мы используем официальный манифест MetalLB, доступный на GitHub.
1.1. Установка MetalLB с готовым шаблоном

    Примените официальный манифест MetalLB для версии 0.14.8 (последняя стабильная на момент 2025 года):

bash
kubectl apply -f https://raw.githubusercontent.com/metallb/metallb/v0.14.8/config/manifests/metallb-native.yaml

temp.sh: line 1: kubectl: command not found

Что делает этот манифест:

    Создает namespace metallb-system.
    Разворачивает:
        Deployment для контроллера MetalLB.
        DaemonSet для speaker'ов (агентов, объявляющих IP-адреса).
        Необходимые RBAC-роли и ServiceAccount.
        Secret для внутренней коммуникации (memberlist).

    Проверьте, что поды MetalLB запустились:

bash
kubectl get pods -n metallb-system

Ожидаемый вывод:
text
NAME                          READY   STATUS    RESTARTS   AGE
controller-xxx                1/1     Running   0          2m
speaker-xxx                   1/1     Running   0          2m
...
1.2. Настройка пула IP-адресов

Создайте файл metallb-config.yaml для конфигурации пула IP-адресов и L2-рекламы:
metallb-config.yaml
yaml

Замечание: Убедитесь, что диапазон 192.168.1.100-192.168.1.120 доступен в вашей сети и не конфликтует с другими устройствами. Это должны быть IP-адреса, достижимые из внешней сети, где клиенты будут подключаться к Redis.

Примените:
bash
kubectl apply -f metallb-config.yaml

Проверьте пул:
bash
kubectl get ipaddresspool -n metallb-system

temp.sh: line 1: kubectl: command not found

Ожидаемый вывод:
text
NAME         AGE
redis-pool   1m
Шаг 2: Создание namespace для Redis

Создайте манифест redis-namespace.yaml:
redis-namespace.yaml
yaml

Примените:
bash
kubectl apply -f redis-namespace.yaml

Проверьте:
bash
kubectl get ns

Ожидаемый вывод:
text
NAME              STATUS   AGE
redis             Active   1m
...

Шаг 3: Развертывание Redis-кластера

Мы создадим Redis-кластер с 6 нодами (3 мастера + 3 реплики) с помощью StatefulSet для управления нодами и сервисами для внутреннего и внешнего доступа.
3.1. Создание ConfigMap для конфигурации Redis

Создайте файл redis-config.yaml:
redis-config.yaml
yaml

Объяснение:

    cluster-enabled yes: Включает кластерный режим.
    appendonly no: Отключает AOF (данные только в памяти).
    save "": Отключает RDB-снимки.
    requirepass: Задает пароль для защиты.

Примените:
bash
kubectl apply -f redis-config.yaml
3.2. Создание StatefulSet для Redis-ноды

Создайте файл redis-statefulset.yaml:
redis-statefulset.yaml
yaml

Объяснение:

    replicas: 6: 6 нод (3 мастера + 3 реплики).
    serviceName: redis-cluster: Связывает StatefulSet с headless-сервисом.
    image: redis:7.0: Официальный образ Redis.
    args: Указывает путь к конфигурации.
    volumeClaimTemplates: []: Без томов, так как данные в памяти.

Примените:
bash
kubectl apply -f redis-statefulset.yaml
3.3. Создание headless-сервиса для внутреннего доступа

Создайте файл redis-service-headless.yaml:
redis-service-headless.yaml
yaml

Объяснение:

    clusterIP: None: Headless-сервис для прямого доступа к подам через DNS (например, redis-cluster-0.redis-cluster.redis.svc.cluster.local).
    Порты: 6379 (Redis), 16379 (кластерный протокол).

Примените:
bash
kubectl apply -f redis-service-headless.yaml