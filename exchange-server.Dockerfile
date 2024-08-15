FROM node:20.16-bookworm-slim

WORKDIR /app

COPY abx_exchange_server/main.js .

CMD [ "node", "main.js" ]
