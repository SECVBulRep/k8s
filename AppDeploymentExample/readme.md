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

