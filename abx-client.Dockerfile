FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder

WORKDIR /app

COPY . .

RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final

WORKDIR /app

COPY --from=builder /app/out .

CMD [ "dotnet", "ABXConsoleClient.dll" ]
