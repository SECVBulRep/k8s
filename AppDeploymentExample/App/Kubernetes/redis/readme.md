1) инициализация кластера:
powershell
redis-cli --cluster create \
  172.16.29.110:6379 172.16.29.111:6379 172.16.29.112:6379 \
  172.16.29.113:6379 172.16.29.114:6379 172.16.29.115:6379 \
  --cluster-replicas 0 -a your-secure-password


