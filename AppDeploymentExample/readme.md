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

redis/
├── namespace.yaml
├── metallb-ip-pool.yaml
├── redis-headless-svc.yaml
├── redis-statefulset.yaml
├── redis-lb.yaml


⚙️ Применение
1. Применить все манифесты:

kubectl apply -f redis/namespace.yaml
kubectl apply -f redis/metallb-ip-pool.yaml
kubectl apply -f redis/redis-headless-svc.yaml
kubectl apply -f redis/redis-lb.yaml
kubectl apply -f redis/redis-statefulset.yaml

2. Убедиться, что поды запущены:

kubectl get pods -n redis-cluster

🤖 Если автоматическая сборка не прошла

Выполни вручную (один раз):

kubectl exec -n redis-cluster -it redis-0 -- redis-cli --cluster create \
  redis-0.redis-headless.redis-cluster.svc.cluster.local:6379 \
  redis-1.redis-headless.redis-cluster.svc.cluster.local:6379 \
  redis-2.redis-headless.redis-cluster.svc.cluster.local:6379 \
  --cluster-replicas 0

🧪 Проверка

kubectl exec -n redis-cluster redis-0 -- redis-cli -p 6379 cluster info

Должно быть:

cluster_state:ok