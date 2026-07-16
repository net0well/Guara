# Infra — Guará (stack mínimo)

Infraestrutura em Docker para a VPS (deploy via **Portainer**). Stack **novo e independente** (`guara-*`) — não reaproveita a infra anterior.

## Princípio: "o storage é a fila"

O Guará segue o modelo do Hangfire: **o banco é a fila de jobs** (persistência + aquisição atômica com lease). Logo, **não há broker** (RabbitMQ removido) e o mínimo para rodar é:

| Serviço | Container | Porta host | Papel |
|---|---|---|---|
| PostgreSQL 16 | `guara-postgres` | `5435` | Storage/fila de jobs + lock distribuído (advisory) + push (LISTEN/NOTIFY) |
| Seq | `guara-seq` | `5342` | **Logs estruturados** (Serilog → Seq) |
| Nginx | `guara-nginx` | `80` | Proxy do dashboard/API/SSE (e `/seq/`) |

A app (`guara-api:8080`) é publicada depois, na mesma rede `guara-network`.

## Deploy no Portainer

1. Derrube a stack anterior no Portainer.
2. **Stacks → Add stack → Web editor** e cole [`docker-compose.yml`](docker-compose.yml).
3. Suba. As rotas `/guara*` dão **502** até publicar a app; `/seq/` já funciona (login inicial: senha `123456`).

## Verificar (opcional, local)

Se quiser validar/subir localmente para teste (precisa de Docker):

```bash
docker compose -f Infra/docker-compose.yml config   # valida o YAML
docker compose -f Infra/docker-compose.yml up -d     # sobe o stack mínimo
```

## Publicando a aplicação depois

- Container `guara-api`, porta interna `8080`, conectado à rede `guara-network` (em outra stack, declare a rede como `external: true`).
- Acesso: dashboard/API em `http://SEU_IP/guara/`, docs `/scalar`, logs `/seq/`.

## Serviços opcionais (habilite quando ligar o recurso)

Não entram no stack mínimo. Para usar, adicione o bloco em `services:` (e o volume) e ajuste o `appsettings`.

### Redis (só se usar `Guara.Storage.Redis` ou cache distribuído)
```yaml
  redis:
    image: redis:7
    container_name: guara-redis
    restart: unless-stopped
    command: redis-server --requirepass ${REDIS_PASSWORD:-guara_dev}
    ports: [ "6380:6379" ]
    volumes: [ "guara_redis_data:/data" ]
    networks: [ guara-network ]
# volumes: guara_redis_data:
```

### Jaeger (só se habilitar tracing OpenTelemetry — `Guara:Telemetry:Enabled=true`)
```yaml
  jaeger:
    image: jaegertracing/all-in-one:latest
    container_name: guara-jaeger
    restart: unless-stopped
    environment: [ "COLLECTOR_OTLP_ENABLED=true" ]
    ports: [ "16687:16686", "4319:4317", "4320:4318" ]
    networks: [ guara-network ]
```
E no nginx (opcional): rota `/jaeger/` → `http://jaeger:16686`.

### Mailpit (só se enviar e-mail de notificação de job)
```yaml
  mailpit:
    image: axllent/mailpit:latest
    container_name: guara-mailpit
    restart: unless-stopped
    ports: [ "1025:1025", "8025:8025" ]
    networks: [ guara-network ]
```

## Segredos (importante para open-source)

Os valores no compose/`appsettings.json` são do **seu** ambiente. Antes de abrir o código, **não publique segredos reais** — sobrescreva via env do container `guara-api` (o .NET mapeia `__` para `:`):

```
Guara__Storage__ConnectionString=Host=guara-postgres;Port=5432;Database=guara;Username=sa_user;Password=...
Authentication__Jwt__Key=<64+ hex aleatório>
```

> Dentro da rede Docker, prefira **nomes de serviço** (`guara-postgres:5432`, `guara-seq:80`) em vez do IP público — mais rápido e não sai pela internet.
