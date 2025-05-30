1) Сбор образа и пуш 
docker build -t secvbulrep/api_weather:latest .
docker push secvbulrep/api_weather:latest


2) просмотр всех ingress   контроллеров
kubectl get pods -n ingress-nginx -o wide

3) просмотр на каком порту висит контроллер на нодах
kubectl -n ingress-nginx get svc ingress-nginx-controller

4. Поды запущены?

kubectl get pods -A -l app.kubernetes.io/name=ingress-nginx
kubectl get pods -l app=api-weather

5) проверка работы
curl -H "Host: api-weather.local" http://api-weather.local:31519/weatherforecast


6)  добавим секрет 
kubectl apply -f postgres-secret.yaml


7) пересобрат образ

docker build -t secvbulrep/api_weather:latest .
docker push secvbulrep/api_weather:latest

8)  логи
 kubectl logs -l app=api-weather 


 9) Установка redis

Шаг 1: Установка MetalLB

kubectl apply -f https://raw.githubusercontent.com/metallb/metallb/v0.13.10/config/manifests/metallb-native.yaml

Шаг 2: Настройка IP-адресов для MetalLB
📌 Зачем?

MetalLB должен знать, какие IP-адреса он может выдавать LoadBalancer-сервисам.

Создай манифест metallb-ip-pool.yaml:

apiVersion: metallb.io/v1beta1
kind: IPAddressPool
metadata:
  namespace: metallb-system
  name: redis-pool
spec:
  addresses:
    - 192.168.1.240-192.168.1.250  # замените на свободные IP из вашей сети
---
apiVersion: metallb.io/v1beta1
kind: L2Advertisement
metadata:
  namespace: metallb-system
  name: redis-adv

IPAddressPool — пул доступных IP

apiVersion: metallb.io/v1beta1
kind: IPAddressPool
metadata:
  namespace: metallb-system
  name: redis-pool

Что происходит здесь:
Поле	Значение
apiVersion	API MetalLB (v1beta1) — используется для всех CRD-объектов
kind	IPAddressPool — определяет диапазон IP-адресов, которые MetalLB может выдавать
namespace	metallb-system — MetalLB работает только в своём namespace
name	redis-pool — имя пула (можно задать любое, например main-pool)

spec:
  addresses:
    - 192.168.1.240-192.168.1.250  # замените на свободные IP из вашей сети

Объяснение:

    addresses: — список диапазонов IP, из которых MetalLB будет назначать IP-адреса сервисам типа LoadBalancer

    192.168.1.240-192.168.1.250 — 11 IP-адресов из вашей локальной сети

🔔 Важно:

    Эти IP не должны быть уже заняты другими серверами, роутерами, DHCP и т.д.

    Они должны быть в одной подсети, что и worker-ноды Kubernetes, чтобы трафик до них доходил напрямую (L2 ARP-режим)

🔹 ЧАСТЬ 2: L2Advertisement — как MetalLB объявляет IP

apiVersion: metallb.io/v1beta1
kind: L2Advertisement
metadata:
  namespace: metallb-system
  name: redis-adv

Что происходит здесь:
Поле	Значение
kind: L2Advertisement	Указывает, что MetalLB будет объявлять IP по протоколу L2 (ARP)
name: redis-adv	Имя ресурса
namespace: metallb-system	Обязательно, потому что MetalLB CRD живут в этом пространстве имён
🧠 Что делает L2Advertisement:

    В режиме Layer 2 (L2) MetalLB работает как "виртуальная сетевая карта", которая:

        Отвечает на ARP-запросы в локальной сети

        Говорит: "IP 192.168.1.240 принадлежит вот этому MAC-адресу (worker-нода №X)"

        Таким образом, весь трафик на 192.168.1.240 пойдёт в Kubernetes-кластер на соответствующую ноду

Убедись, что ресурсы созданы

kubectl get ipaddresspool -n metallb-system
kubectl get l2advertisement -n metallb-system


Шаг 3: Создание namespace для Redis
📌 Зачем?

Чтобы изолировать Redis и не мешать другим приложениям.

apiVersion: v1
kind: Namespace
metadata:
  name: redis-cluster

kubectl apply -f namespace.yaml

 Шаг 4: Headless-сервис для Redis
📌 Зачем?

Redis Cluster требует видеть все свои pod'ы по DNS-именам (redis-0, redis-1, ...). Headless-сервис нужен, чтобы создать DNS-записи на каждый pod.

apiVersion: v1
kind: Service
metadata:
  name: redis-headless         # имя сервиса
  namespace: redis-cluster     # namespace, куда устанавливается
  labels:                      # 🟢 labels должны быть внутри metadata!
    app: redis
spec:
  clusterIP: None              # ключевая часть: headless-сервис
  selector:
    app: redis
  ports:
    - name: redis
      port: 6379
    - name: cluster-bus
      port: 16379


kubectl apply -f redis-headless-svc.yaml


Шаг 5: StatefulSet для Redis Cluster
📌 Зачем?

Redis Cluster требует стабильных имён pod'ов (например, redis-0), поэтому мы используем StatefulSet, а не Deployment.

apiVersion: apps/v1
kind: StatefulSet
metadata:
  name: redis
  namespace: redis-cluster
spec:
  serviceName: redis-headless   # важно: имя headless-сервиса
  replicas: 3                   # три узла для Redis Cluster (мастера)
  selector:
    matchLabels:
      app: redis
  template:
    metadata:
      labels:
        app: redis
    spec:
      containers:
        - name: redis
          image: bitnami/redis-cluster:7.2  # образ Redis с кластерной поддержкой
          ports:
            - containerPort: 6379           # основной порт Redis
              name: redis
            - containerPort: 16379          # кластерный bus-порт Redis
              name: cluster-bus
          env:
            - name: ALLOW_EMPTY_PASSWORD
              value: "yes"

            # Ключевая переменная — список всех узлов, участвующих в кластере
            - name: REDIS_NODES
              value: "redis-0.redis-headless.redis-cluster.svc.cluster.local redis-1.redis-headless.redis-cluster.svc.cluster.local redis-2.redis-headless.redis-cluster.svc.cluster.local"

            # Указывает, кто должен создать кластер
            - name: REDIS_CLUSTER_CREATOR
              value: "yes"

            # IP, который Redis будет "анонсировать" для кластера
            - name: REDIS_CLUSTER_ANNOUNCE_IP
              valueFrom:
                fieldRef:
                  fieldPath: status.podIP

            - name: REDIS_CLUSTER_ANNOUNCE_PORT
              value: "6379"
            - name: REDIS_CLUSTER_ANNOUNCE_BUS_PORT
              value: "16379"
            - name: REDIS_CLUSTER_DYNAMIC_IPS
              value: "yes"


kubectl apply -f redis-statefulset.yaml

