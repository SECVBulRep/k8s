для тестирования сети юзай утилиту
kubectl run -t -i --rm --image amouat/network-utils test bash

root@test:/# curl my-service


для того тчо заработал Ingress

установил контроллер 
kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-v1.10.1/deploy/static/provider/baremetal/deploy.yaml

прописал в хотсах

172.16.29.23 my-app.local

и проверил

curl http://my-app.local:32424

в хост файле можно прописать только одну ноду и все равно будет работать.

✅ Как это работает:

    Ты отправляешь запрос на 172.16.29.21:32424 (мастер-нода, где нет ingress-контроллера).

    kube-proxy (сетевой компонент Kubernetes) на этой ноде перехватывает входящий трафик на порт 32424.

    kube-proxy по iptables или IPVS правилам перенаправляет трафик на ноду, где реально запущен ingress-контроллер (vbox2, 172.16.29.23), или даже сразу в нужный Pod.

    Kubernetes проксирует NodePort кластер-wide — это и есть его фича.