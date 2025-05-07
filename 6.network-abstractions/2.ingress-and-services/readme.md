для тестирования сети юзай утилиту
kubectl run -t -i --rm --image amouat/network-utils test bash

root@test:/# curl my-service


для того тчо заработал Ingress

установил контроллер 
kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-v1.10.1/deploy/static/provider/baremetal/deploy.yaml

прописал в хотсах

172.16.29.23 my-app.local

172.16.29.23(потому что именно на этой ноде запустился контролллер)


и проверил

 curl http://my-app.local:32424
