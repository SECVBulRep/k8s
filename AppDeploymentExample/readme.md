1) Сбор образа и пуш 

docker login 172.16.29.104:8083
docker login 172.16.29.104:8082


для api_weather cd C:\Projects\MyGit\k8s\AppDeploymentExample\App


docker build -f HostnameApi/Dockerfile -t wm/api_weather:latest .
docker tag wm/api_weather:latest 172.16.29.104:8082/api_weather:latest
docker push 172.16.29.104:8082/api_weather:latest




docker build -f WeatherProxy/Dockerfile -t wm/weather-proxy:latest .
docker tag wm/weather-proxy:latest 172.16.29.104:8082/weather-proxy:latest
docker push 172.16.29.104:8082/weather-proxy:latest


kubectl create secret docker-registry nexus-secret \
  --docker-server=172.16.29.104:8083 \
  --docker-username=developer \
  --docker-password=developer123 \
  --docker-email=developer@company.com


kubectl patch serviceaccount default -p '{"imagePullSecrets": [{"name": "nexus-secret"}]}'

2) просмотр всех ingress   контроллеров
kubectl get pods -n ingress-nginx -o wide

3) просмотр на каком порту висит контроллер на нодах
kubectl -n ingress-nginx get svc ingress-nginx-controller

4. Поды запущены?

kubectl get pods -A -l app.kubernetes.io/name=ingress-nginx
kubectl get pods -l app=api-weather

5) проверка работы
curl -H "Host: api-weather.local" http://api-weather.local:31519/weatherforecast
curl.exe -H "Host: weather-proxy.local" http://weather-proxy.local/proxy-weather

8)  логи
 kubectl logs -l app=api-weather 


