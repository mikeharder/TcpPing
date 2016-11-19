# Build 
```
cd TcpPing
docker build -t tcpping -f [Dockerfile|Dockerfile.nano] .
```

# Run
```
docker run -it --rm -p 5201:5201 tcpping [-s] [-c server parallel]
```
