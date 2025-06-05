1.1. Установка MetalLB

    Добавьте namespace для MetalLB:

bash
kubectl apply -f https://raw.githubusercontent.com/metallb/metallb/v0.14.8/config/manifests/metallb-native.yaml


Это создаст namespace metallb-system и развернет компоненты MetalLB.

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
yaml
apiVersion: metallb.io/v1beta1
kind: IPAddressPool
metadata:
  name: redis-pool
  namespace: metallb-system
spec:
  addresses:
  - 192.168.1.100-192.168.1.120  # Замените на ваш диапазон IP-адресов
---
apiVersion: metallb.io/v1beta1
kind: L2Advertisement
metadata:
  name: redis-l2
  namespace: metallb-system
spec:
  ipAddressPools:
  - redis-pool

Замечания:

    Убедитесь, что диапазон IP-адресов (192.168.1.100-192.168.1.120) доступен в вашей сети и не конфликтует с другими устройствами.
    Эти IP должны быть достижимы из внешней сети, где клиенты будут подключаться к Redis.

Примените конфигурацию:
bash
kubectl apply -f metallb-config.yaml

Проверьте, что пул создан:
bash
kubectl get ipaddresspool -n metallb-system

Ожидаемый вывод:
text
NAME         AGE
redis-pool   1m

Шаг 2: Создание namespace для Redis

Создайте namespace redis для изоляции ресурсов:
bash
kubectl create namespace redis

Проверьте:
bash
kubectl get ns

Ожидаемый вывод:
text
NAME              STATUS   AGE
redis             Active   1m
...