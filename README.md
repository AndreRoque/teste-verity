# CashFlow - Controle de Fluxo de Caixa

Sistema de controle de fluxo de caixa diário para comerciantes, composto por dois microsserviços em .NET 10. Permite registrar lançamentos financeiros (débitos e créditos) e consultar o saldo consolidado por dia, com autenticação via Keycloak.

> Detalhes de arquitetura, decisões técnicas, modelo de domínio e design de APIs estão documentados em [docs/SDD.md](docs/SDD.md).

---

## Serviços

| Serviço | Porta |
|---------|-------|
| Transactions API | 8080 |
| Consolidation API | 8081 |
| Keycloak (Identity Provider) | 9080 |
| RabbitMQ Management UI | 15672 |

---

## Como Rodar

### Pré-requisitos
- Docker 24+
- Docker Compose v2
- `make` (Linux/macOS: nativo; Windows: via [Chocolatey](https://chocolatey.org/) com `choco install make` ou WSL)

### Comandos Make

| Comando | Descrição |
|---------|-----------|
| `make up` | Builda as imagens e sobe toda a solução |
| `make up-infra` | Sobe apenas a infra (Postgres, RabbitMQ, Redis, Keycloak) — útil para rodar as APIs localmente |
| `make down` | Para todos os containers, mantendo os volumes |
| `make down-clean` | Para e remove todos os volumes (dados apagados) |
| `make build` | Compila a solution .NET em Release |
| `make test` | Executa os testes |
| `make logs` | Acompanha os logs em tempo real |

### Subir todos os serviços

```bash
make up
```

O **Keycloak demora ~90 segundos** para inicializar e importar o realm — as APIs só sobem após ele estar pronto.

```bash
docker compose ps   # aguarde todos os serviços com status "healthy"
```

### Verificar saúde

```bash
curl http://localhost:8080/health
curl http://localhost:8081/health
curl http://localhost:9080/health/ready
```

### Parar

```bash
make down          # mantém volumes
make down-clean    # remove volumes também
```

---

## Como Rodar as APIs Localmente (fora do Docker)

Útil para depurar as APIs na IDE. A infraestrutura continua no Docker.

### Pré-requisitos
- .NET 10 SDK
- Docker (para a infraestrutura)

### 1. Subir apenas a infraestrutura

```bash
make up-infra
```

Sem `make`, use: `docker compose up -d postgres-transactions postgres-consolidation rabbitmq redis keycloak`

Se as APIs estiverem rodando no Docker, pare antes os containers delas para liberar as portas 8080/8081: `docker compose stop transactions-api consolidation-api`.

### 2. Rodar os serviços

Compile uma vez antes, para que as duas APIs não disputem os mesmos arquivos de build:

```bash
dotnet build CashFlow.slnx

# Terminal 1
cd src/CashFlow.Transactions
dotnet run --no-build

# Terminal 2
cd src/CashFlow.Consolidation
dotnet run --no-build
```

As APIs sobem nas mesmas portas do Docker (Transactions em `8080`, Consolidation em `8081`), e os exemplos deste README valem para os dois modos. As migrations são aplicadas automaticamente na inicialização.

Os arquivos `src/CashFlow.Transactions/CashFlow.Transactions.http` e `src/CashFlow.Consolidation/CashFlow.Consolidation.http` trazem requisições prontas, incluindo a obtenção do token, para o Visual Studio, o Rider ou a extensão REST Client do VS Code.

---

## Autenticação

Todos os endpoints exigem um token JWT obtido do Keycloak.

### Obter token

O token expira em **5 minutos**. Se as requisições começarem a retornar `401`, gere um novo.

**Windows (PowerShell):**

```powershell
$TOKEN = (Invoke-RestMethod -Method Post `
  -Uri "http://localhost:9080/realms/cashflow/protocol/openid-connect/token" `
  -Body "grant_type=password&client_id=cashflow-api&client_secret=cashflow-api-secret&username=cashflow-admin&password=admin123").access_token
```

> No PowerShell, `curl` é um alias de `Invoke-WebRequest` e não aceita a sintaxe do curl real, por isso os exemplos para Windows usam `Invoke-RestMethod`. Para copiar o token direto para a área de transferência (útil no Swagger): `$TOKEN | Set-Clipboard`.

**Linux / macOS / Git Bash** (requer [`jq`](https://jqlang.github.io/jq/)):

```bash
TOKEN=$(curl -s -X POST http://localhost:9080/realms/cashflow/protocol/openid-connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password&client_id=cashflow-api&client_secret=cashflow-api-secret&username=cashflow-admin&password=admin123" \
  | jq -r '.access_token')
```

Para usar outro usuário, troque `username` e `password` no corpo da requisição.

### Usuários disponíveis

| Usuário | Senha | Acesso |
|---------|-------|--------|
| `cashflow-admin` | `admin123` | Leitura e escrita |
| `cashflow-reader` | `reader123` | Apenas leitura |

Keycloak Admin UI: http://localhost:9080/admin (admin / admin)

---

## Endpoints

### Swagger

- Transactions: http://localhost:8080/swagger
- Consolidation: http://localhost:8081/swagger

1. Gere um token (seção [Obter token](#obter-token))
2. Clique em **Authorize** (cadeado) e cole o token **sem** o prefixo `Bearer `. O Swagger adiciona o prefixo sozinho
3. Em cada endpoint, use **Try it out** → **Execute**

O mesmo token vale para os dois serviços, mas é preciso autorizar em cada Swagger.

### Transactions API — http://localhost:8080

**Windows (PowerShell):**

```powershell
$headers = @{ Authorization = "Bearer $TOKEN" }

# Criar lançamento
Invoke-RestMethod -Method Post -Uri "http://localhost:8080/api/v1/transactions" -Headers $headers `
  -ContentType "application/json" `
  -Body '{"amount": 1500.00, "type": "credit", "description": "Venda de produto", "occurredAt": "2026-10-19T10:30:00Z"}'

# Listar lançamentos
Invoke-RestMethod -Uri "http://localhost:8080/api/v1/transactions" -Headers $headers

# Listar por data
Invoke-RestMethod -Uri "http://localhost:8080/api/v1/transactions?date=2026-10-19" -Headers $headers
```

**Linux / macOS / Git Bash:**

```bash
# Criar lançamento
curl -X POST http://localhost:8080/api/v1/transactions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"amount": 1500.00, "type": "credit", "description": "Venda de produto", "occurredAt": "2026-10-19T10:30:00Z"}'

# Listar lançamentos
curl http://localhost:8080/api/v1/transactions -H "Authorization: Bearer $TOKEN"

# Listar por data
curl "http://localhost:8080/api/v1/transactions?date=2026-10-19" -H "Authorization: Bearer $TOKEN"
```

### Consolidation API — http://localhost:8081

**Windows (PowerShell):**

```powershell
# Consultar saldo consolidado por data
Invoke-RestMethod -Uri "http://localhost:8081/api/v1/consolidation/2026-10-19" -Headers $headers
```

**Linux / macOS / Git Bash:**

```bash
# Consultar saldo consolidado por data
curl http://localhost:8081/api/v1/consolidation/2026-10-19 -H "Authorization: Bearer $TOKEN"
```

> O consolidado é atualizado de forma assíncrona e a consulta tem cache de 60 segundos: um lançamento recém-criado pode levar até 1 minuto para aparecer no saldo.

---

## Testes

```bash
dotnet test
```

9 testes passando (4 em Transactions, 5 em Consolidation).
