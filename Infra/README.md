# Infra — Guará (stack mínimo)

Infraestrutura em Docker para a VPS (deploy via **Portainer**). Stack **novo e independente** (`guara-*`) — não reaproveita a infra anterior.

## Princípios: "o storage é a fila" e zero terceiros por padrão

O Guará segue o modelo do Hangfire: **o banco é a fila de jobs** (persistência + aquisição atômica com lease) — não há broker. E, pelo [ADR-0009](../docs/adr/0009-politica-de-dependencias.md), o framework é **agnóstico a ferramentas de terceiros**: logs são **estruturados em JSON no stdout** via `Microsoft.Extensions.Logging` (nativo do .NET), consumíveis por qualquer coletor. O mínimo para rodar é:

| Serviço | Container | Porta host | Papel |
|---|---|---|---|
| PostgreSQL 16 | `guara-postgres` | `5435` | Storage/fila de jobs + lock distribuído (advisory) + push (LISTEN/NOTIFY) |
| Nginx | `guara-nginx` | `80` | Proxy do dashboard/API/SSE |

A app (`guara-api:8080`) é publicada depois, na mesma rede `guara-network`. Os logs dela aparecem direto no Portainer (**Containers → guara-api → Logs**) como JSON estruturado.

## Deploy no Portainer

1. Derrube a stack anterior no Portainer.
2. **Stacks → Add stack → Web editor** e cole [`docker-compose.yml`](docker-compose.yml).
3. Suba. As rotas `/guara*` dão **502** até publicar a app.

## Verificar (opcional, local)

Se quiser validar/subir localmente para teste (precisa de Docker):

```bash
docker compose -f Infra/docker-compose.yml config   # valida o YAML
docker compose -f Infra/docker-compose.yml up -d     # sobe o stack mínimo
```

## Publicando a aplicação depois

- Container `guara-api`, porta interna `8080`, conectado à rede `guara-network` (em outra stack, declare a rede como `external: true`).
- Acesso: dashboard/API em `http://SEU_IP/guara/`, docs `/scalar`.

## Serviços opcionais (habilite quando quiser o recurso)

Não entram no stack mínimo — o Guará funciona sem eles. Para usar, adicione o bloco em `services:` (e o volume, quando houver).

### Seq (painel de logs estruturados — opcional)

O app já emite JSON estruturado no stdout; um painel como o Seq é **escolha sua**, não dependência. Para usá-lo, adicione o serviço e configure um forwarder/sink no **seu host** (ex.: Serilog ou OpenTelemetry Logs — fora do framework):

```yaml
  seq:
    image: datalust/seq:latest
    container_name: guara-seq
    restart: unless-stopped
    environment:
      - ACCEPT_EULA=Y
      - SEQ_FIRSTRUN_ADMINPASSWORD=${SEQ_ADMIN_PASSWORD:-guara_dev}
    ports: [ "5342:80" ]
    volumes: [ "guara_seq_data:/data" ]
    networks: [ guara-network ]
# volumes: guara_seq_data:
```

### Redis (só se usar o acelerador `Guara.Redis` ou cache distribuído)
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

Os valores no compose/`appsettings.json` são de **desenvolvimento**. Em produção, sobrescreva via env do container `guara-api` (o .NET mapeia `__` para `:`):

```
Guara__Storage__ConnectionString=Host=guara-postgres;Port=5432;Database=guara;Username=guara;Password=...
Authentication__Jwt__Key=<64+ hex aleatório>
```

> Dentro da rede Docker, prefira **nomes de serviço** (`guara-postgres:5432`) em vez do IP público — mais rápido e não sai pela internet.
