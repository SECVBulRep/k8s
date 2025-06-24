1) Сбор образа и пуш 

для api_weather cd C:\Projects\MyGit\k8s\AppDeploymentExample\App

docker build -f HostnameApi/Dockerfile -t secvbulrep/api_weather:latest .
docker push secvbulrep/api_weather:latest


docker build -f WeatherProxy/Dockerfile -t secvbulrep/weather-proxy:latest .
docker push secvbulrep/weather-proxy:latest


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


