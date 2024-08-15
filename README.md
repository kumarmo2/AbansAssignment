
## Pre-requisites For Building
1. docker

## How To Run
1. Run below command
    - ```git clone git@github.com:kumarmo2/AbansAssignment.git abans-assignment && cd  abans-assignment```
2. [Build Images](#build-images) for [Exchange-Server](#abx-server) and [ABX-Client-Console-App](#abx-client)
3. Create the docker network
	- ```docker network create abx-network```
4. Start the ABX Exchange server
	-  From the root of the project, in the terminal run:
	    ```
        docker run --rm --name abx-server -p 3000:3000 --network abx-network abx-server 
        ```
	- This will run the container in attached mode, and in the terminal you should be able to monitor the logs for the exchange.
5. Start the ABX client console app
	1. In the another terminal, run the below command from the root of the project:
	      ```
          docker run --rm --name abx-client -v "./dist:/app/dist" --network abx-network abx-client
          ```
	  2. This will run the container in attached mode, and in the terminal you should be able to monitor the logs of the client.
	  3. If everything runs correctly, the container will shut down and the json output should be present in `dist` directory at the root of the 



## <a name="build-images"></a> Build Images

### <a name="abx-server"></a>Build ABX Server

From root of the project, run the below command
```
docker build -f exchange-server.Dockerfile -t abx-server . 
```

### <a name="abx-client"></a>Build ABX Client(Main Application)

From the root of the project, run the below command
```
docker build -f abx-client.Dockerfile -t abx-client .
```
