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