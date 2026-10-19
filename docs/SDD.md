# Software Design Document (SDD)
## CashFlow — Sistema de Controle de Fluxo de Caixa

| Campo | Valor |
|---|---|
| Versão | 1.0 |
| Data | 2026-10-18 |
| Status | Aprovado |
| Autor | André Roque |
| Tecnologia Principal | .NET 10, RabbitMQ, PostgreSQL, Redis, Keycloak |

---

## Índice

1. [Introdução](#1-introdução)
2. [Escopo e Contexto de Negócio](#2-escopo-e-contexto-de-negócio)
3. [Stakeholders](#3-stakeholders)
4. [Requisitos Funcionais Refinados](#4-requisitos-funcionais-refinados)
5. [Requisitos Não-Funcionais com Metas Mensuráveis](#5-requisitos-não-funcionais-com-metas-mensuráveis)
6. [Domínios Funcionais e Capacidades de Negócio](#6-domínios-funcionais-e-capacidades-de-negócio)
7. [Arquitetura da Solução](#7-arquitetura-da-solução)
8. [Modelo de Domínio](#8-modelo-de-domínio)
9. [Design de APIs](#9-design-de-apis)
10. [Estratégia de Integração e Comunicação](#10-estratégia-de-integração-e-comunicação)
11. [Modelo de Dados](#11-modelo-de-dados)
12. [Estratégia de Segurança](#12-estratégia-de-segurança)
13. [Estratégia de Resiliência](#13-estratégia-de-resiliência)
14. [Observabilidade e Monitoramento](#14-observabilidade-e-monitoramento)
15. [Arquitetura de Deploy](#15-arquitetura-de-deploy)
16. [Architecture Decision Records (ADRs)](#16-architecture-decision-records-adrs)
17. [Arquitetura de Transição](#17-arquitetura-de-transição)
18. [Estimativa de Custos](#18-estimativa-de-custos)
19. [Melhorias Futuras e Roadmap](#19-melhorias-futuras-e-roadmap)
20. [Glossário](#20-glossário)

---

## 1. Introdução

### 1.1 Propósito

Este documento descreve a solução técnica completa para o sistema **CashFlow**, um sistema de controle de fluxo de caixa diário para comerciantes. O documento serve como referência arquitetural para desenvolvimento, operação e evolução futura do sistema.

### 1.2 Objetivos do Sistema

- Permitir que comerciantes registrem lançamentos financeiros (débitos e créditos) em tempo real
- Disponibilizar relatório de saldo consolidado por dia
- Garantir que o registro de lançamentos nunca seja interrompido por falhas no módulo de consolidação
- Suportar picos de carga de 50 requisições/segundo no serviço de consolidação

### 1.3 Abreviações e Siglas

| Sigla | Significado |
|---|---|
| CQRS | Command Query Responsibility Segregation |
| EDA | Event-Driven Architecture |
| SDD | Software Design Document |
| ADR | Architecture Decision Record |
| NFR | Non-Functional Requirement |
| TTL | Time To Live |
| RPS | Requests Per Second |
| DLQ | Dead Letter Queue |
| DTO | Data Transfer Object |
| EF Core | Entity Framework Core |
| JWT | JSON Web Token |
| OIDC | OpenID Connect |
| IdP | Identity Provider |

---

## 2. Escopo e Contexto de Negócio

### 2.1 Problema de Negócio

Um comerciante opera um negócio com múltiplas transações financeiras diárias (vendas, pagamentos a fornecedores, despesas operacionais). Hoje, o controle é feito manualmente, gerando:

- Dificuldade de visibilidade do saldo em tempo real
- Risco de erros de lançamento
- Ausência de histórico estruturado
- Impossibilidade de análise de tendências

### 2.2 Solução Proposta

Sistema digital com dois serviços independentes:

1. **Serviço de Lançamentos**: Registro em tempo real de débitos e créditos
2. **Serviço de Consolidado Diário**: Relatório do saldo acumulado por dia

### 2.3 Fora do Escopo (v1.0)

- Multitenancy (múltiplos comerciantes)
- Conciliação bancária automática
- Integração com sistemas de pagamento (ex: PagSeguro, Cielo)
- Relatórios avançados (mensal, semanal, por categoria)
- Exportação de dados (CSV, PDF)

---

## 3. Stakeholders

| Perfil | Interesse | Impacto |
|---|---|---|
| Comerciante | Registrar lançamentos e consultar saldo | Alto — usuário final |
| Equipe de Operações | Monitorar saúde dos serviços | Médio — responsável pela disponibilidade |
| Equipe de Desenvolvimento | Manutenção e evolução | Alto — responsável técnico |
| Contador/Financeiro | Acurácia dos dados consolidados | Alto — dependente do relatório |

---

## 4. Requisitos Funcionais Refinados

### RF-01: Registro de Lançamento

| Campo | Descrição |
|---|---|
| **ID** | RF-01 |
| **Nome** | Registrar Lançamento Financeiro |
| **Descrição** | O sistema deve permitir o registro de lançamentos individuais do tipo débito ou crédito, com valor, data e descrição opcional |
| **Ator** | Comerciante / Sistema Integrador |
| **Pré-condição** | Valor deve ser positivo; tipo deve ser "debit" ou "credit" |
| **Pós-condição** | Lançamento persistido; evento publicado na fila |
| **Regras de negócio** | Valor > 0; timestamp em UTC; tipo case-insensitive |

**Fluxo Principal:**
1. Cliente envia `POST /api/v1/transactions` com payload
2. Sistema valida o payload (tipo, valor)
3. Cria entidade `Transaction` com ID gerado
4. Persiste no banco de dados
5. Publica `TransactionCreatedEvent` no RabbitMQ
6. Retorna `201 Created` com dados do lançamento

**Fluxo Alternativo — Payload inválido:**
- Se `amount <= 0` → retorna `400 Bad Request` com mensagem de erro
- Se `type` inválido → retorna `400 Bad Request` com valores aceitos

---

### RF-02: Consulta de Lançamentos

| Campo | Descrição |
|---|---|
| **ID** | RF-02 |
| **Nome** | Consultar Histórico de Lançamentos |
| **Descrição** | O sistema deve permitir listagem de lançamentos, com filtro opcional por data |
| **Ator** | Comerciante |
| **Parâmetros** | `?date=YYYY-MM-DD` (opcional) |
| **Ordenação** | Decrescente por `occurredAt` |

---

### RF-03: Relatório de Saldo Diário Consolidado

| Campo | Descrição |
|---|---|
| **ID** | RF-03 |
| **Nome** | Consultar Saldo Consolidado por Data |
| **Descrição** | O sistema deve retornar o saldo consolidado de um dia específico, incluindo total de créditos, débitos e saldo líquido |
| **Ator** | Comerciante / Contador |
| **Parâmetros** | `{date}` no formato `YYYY-MM-DD` (path parameter) |
| **Regras** | Saldo = TotalCredits - TotalDebits; se não há lançamentos no dia → 404 |

---

### RF-04: Atualização Automática do Consolidado

| Campo | Descrição |
|---|---|
| **ID** | RF-04 |
| **Nome** | Consolidar Lançamento Automaticamente |
| **Descrição** | A cada novo lançamento registrado, o consolidado do dia correspondente deve ser atualizado automaticamente de forma assíncrona |
| **Ator** | Sistema (consumer RabbitMQ) |
| **Garantia** | At-least-once delivery via MassTransit |
| **Concorrência** | Lançamentos simultâneos do mesmo dia nunca se sobrescrevem: conflitos são detectados por optimistic locking e reprocessados via retry (ver [ADR-009](#adr-009-controle-de-concorrência-no-consolidado)) |

---

## 5. Requisitos Não-Funcionais com Metas Mensuráveis

| ID | Requisito | Meta | Como Medir | Como Atender |
|---|---|---|---|---|
| NFR-01 | **Disponibilidade do serviço de lançamentos** | 99,9% uptime (≤ 8,7h downtime/ano) | Monitoramento /health | Deploy independente; sem dependência síncrona do Consolidation |
| NFR-02 | **Throughput do Consolidation** | 50 RPS sustentados | Teste de carga (k6/JMeter) | Redis cache-aside (suporta >100k RPS) |
| NFR-03 | **Taxa de perda de requisições** | ≤ 5% em pico | % de 5xx / total requests | Redis elimina carga no PostgreSQL em pico |
| NFR-04 | **Latência do Consolidation (p95)** | ≤ 200ms (cache hit ≤ 10ms) | Percentil 95 de tempo de resposta | Redis cache; consulta pré-computada |
| NFR-05 | **Latência do Transactions (p95)** | ≤ 500ms | Percentil 95 | PostgreSQL com índice em `OccurredAt` |
| NFR-06 | **Consistência do consolidado** | Eventual; delay máx. de 5s | Diferença de timestamp entre evento e atualização | RabbitMQ com consumer dedicado |
| NFR-07 | **Durabilidade dos lançamentos** | Zero perda após persistência | Testes de falha de RabbitMQ | PostgreSQL persiste antes do publish; MassTransit com retry |
| NFR-08 | **Escalabilidade horizontal** | Suportar 2x carga sem alteração de código | Teste de carga com 2 instâncias | Stateless APIs; estado apenas nos datastores; optimistic locking no consolidado permite múltiplas instâncias do consumer |
| NFR-09 | **Exatidão do consolidado sob concorrência** | Zero lançamentos perdidos ou sobrescritos | Carga concorrente no mesmo dia comparando soma esperada × consolidado | Optimistic locking (`xmin`) + retry exponencial + consumo serial por instância (ADR-009) |

---

## 6. Domínios Funcionais e Capacidades de Negócio

### 6.1 Mapa de Capacidades

```mermaid
mindmap
  root((Gestão\nFinanceira))
    Lançamentos
      Registrar lançamento
      Validar lançamento
      Consultar histórico
      Publicar evento
    Consolidação
      Calcular saldo diário
      Acumular créditos e débitos
      Servir relatório consolidado
      Cachear resultados
      Consumir eventos
```

### 6.2 Classificação de Subdomínios (DDD)

| Subdomínio | Tipo | Justificativa |
|---|---|---|
| Lançamentos | **Core Domain** | Diferencial competitivo; regras de negócio críticas |
| Consolidação | **Core Domain** | Entrega valor direto ao usuário final |
| Infraestrutura de Mensageria | **Generic Subdomain** | Capacidade genérica (commodity) |
| Cache | **Generic Subdomain** | Capacidade genérica (commodity) |

### 6.3 Bounded Contexts e Linguagem Ubíqua

**Contexto: Lançamentos (Transactions)**

| Termo | Definição no Contexto |
|---|---|
| `Transaction` | Um registro financeiro único de débito ou crédito |
| `Amount` | Valor monetário positivo do lançamento |
| `Type` | Classificação: `debit` (saída) ou `credit` (entrada) |
| `OccurredAt` | Momento real do evento financeiro (UTC) |

**Contexto: Consolidação (Consolidation)**

| Termo | Definição no Contexto |
|---|---|
| `DailyConsolidation` | Agregado do saldo de um dia específico |
| `Balance` | Saldo líquido = TotalCredits - TotalDebits |
| `TotalCredits` | Soma de todas as entradas do dia |
| `TotalDebits` | Soma de todas as saídas do dia |
| `TransactionCount` | Número de lançamentos processados no dia |

---

## 7. Arquitetura da Solução

### 7.1 Visão Geral — Diagrama de Contexto (C4 Level 1)

```mermaid
flowchart TD
    Comerciante(["Comerciante\nUsuário Final"])
    KC["Keycloak 26\nIdentity Provider\n:9080"]
    APIGW["AWS API Gateway\nHTTP API\nJWT Authorizer · Throttling"]

    subgraph CashFlow["Sistema CashFlow"]
        TX["CashFlow\nTransactions API\n.NET 10 · :8080"]
        CONS["CashFlow\nConsolidation API\n.NET 10 · :8081"]
        MQ[("RabbitMQ\nMessage Broker")]
    end

    Comerciante -->|"1. obtém token JWT"| KC
    Comerciante -->|"2. requisições com Bearer JWT"| APIGW
    APIGW -.->|"valida JWT (JWT Authorizer)"| KC
    APIGW -->|"roteia /transactions"| TX
    APIGW -->|"roteia /consolidation"| CONS
    TX -->|"publica evento"| MQ
    MQ -->|"consome evento\nnão há chamada direta"| CONS
    TX -.->|"busca JWKS na inicialização"| KC
    CONS -.->|"busca JWKS na inicialização"| KC
```

> **Dev vs Prod:** em desenvolvimento (Docker Compose) não há API Gateway — os serviços são acessados diretamente em `:8080` e `:8081`. O gateway existe apenas no ambiente AWS.

### 7.2 Diagrama de Containers (C4 Level 2)

```mermaid
flowchart TD
    subgraph CashFlow["CashFlow System"]
        APIGW["AWS API Gateway\nHTTP API · JWT Authorizer\nThrottling · VPC Link"]
        TX["Transactions API\nASP.NET Core 10 · MediatR\nMassTransit · EF Core · JwtBearer\n:8080"]
        CONS["Consolidation API\nASP.NET Core 10 · MediatR\nMassTransit · EF Core · JwtBearer\nStackExchange.Redis\n:8081"]
        KC["Keycloak 26\nIdentity Provider\nRealm: cashflow\n:9080"]

        PG_TX[("PostgreSQL\ncashflow_transactions\n:5432")]
        PG_CONS[("PostgreSQL\ncashflow_consolidation\n:5433")]
        MQ[("RabbitMQ 3.13\nExchange: CashFlow.Shared.Events:TransactionCreatedEvent\nQueue: TransactionCreated\n:5672 AMQP · :15672 Management")]
        REDIS[("Redis 7\ncache-aside\n:6379")]
    end

    APIGW -->|"roteia /transactions"| TX
    APIGW -->|"roteia /consolidation"| CONS
    APIGW -.->|"valida JWT (JWT Authorizer)"| KC
    TX -->|"persiste lançamentos"| PG_TX
    TX -->|"publica TransactionCreatedEvent"| MQ
    TX -.->|"busca JWKS (startup)"| KC
    MQ -->|"consome async"| CONS
    CONS -->|"persiste consolidados"| PG_CONS
    CONS <-->|"cache TTL 60s"| REDIS
    CONS -.->|"busca JWKS (startup)"| KC
```

### 7.3 Diagrama de Componentes — Transactions (C4 Level 3)

```mermaid
flowchart TD
    subgraph TX["CashFlow.Transactions"]
        subgraph API["API Layer"]
            Controller["TransactionsController\nPOST /api/v1/transactions\nGET /api/v1/transactions"]
        end

        subgraph App["Application Layer · CQRS"]
            CmdH["CreateTransactionHandler\nCommand — valida · persiste · publica"]
            QryH["GetTransactionsHandler\nQuery — filtra · ordena · projeta DTO"]
        end

        subgraph Dom["Domain Layer"]
            Entity["Transaction Entity\nId · Amount · Type\nDescription · OccurredAt"]
            Enum["TransactionType\nDebit = 0 · Credit = 1"]
        end

        subgraph Infra["Infrastructure Layer"]
            DbCtx["TransactionsDbContext\nEF Core · DbSet Transaction\nÍndice em OccurredAt"]
            Pub["IPublishEndpoint\nMassTransit · serialização automática"]
        end
    end

    Controller -->|"CreateTransactionCommand"| CmdH
    Controller -->|"GetTransactionsQuery"| QryH
    CmdH --> Entity
    Entity --> Enum
    CmdH --> DbCtx
    CmdH --> Pub
    QryH --> DbCtx
    DbCtx --> PG[("PostgreSQL")]
    Pub --> MQ[("RabbitMQ")]
```

### 7.4 Diagrama de Componentes — Consolidation (C4 Level 3)

```mermaid
flowchart TD
    subgraph CONS["CashFlow.Consolidation"]
        subgraph API["API Layer"]
            Controller["ConsolidationController\nGET /api/v1/consolidation/{date}"]
        end

        subgraph App["Application Layer · CQRS + Consumer"]
            QryH["GetDailyBalanceHandler\nQuery · cache-aside · retorna DTO"]
            Consumer["TransactionCreatedConsumer\nIConsumer MassTransit · 1 msg por vez\nrecebe · busca · aplica · persiste\nconflito → limpa tracker · retry"]
        end

        subgraph Dom["Domain Layer"]
            Entity["DailyConsolidation Entity\nDate · TotalCredits · TotalDebits\nBalance computed · TransactionCount\nVersion (xmin)"]
        end

        subgraph Infra["Infrastructure Layer"]
            DbCtx["ConsolidationDbContext\nEF Core · Índice único por Date\nConcurrency token xmin"]
            Cache["IDistributedCache\nRedis · TTL 60s\nchave: consolidation:{yyyy-MM-dd}"]
        end
    end

    MQ[("RabbitMQ")] -->|"TransactionCreatedEvent"| Consumer
    Controller -->|"GetDailyBalanceQuery"| QryH
    QryH <-->|"cache-aside"| Cache
    QryH --> DbCtx
    Consumer --> Entity
    Consumer --> DbCtx
    DbCtx --> PG[("PostgreSQL")]
    Cache <--> REDIS[("Redis")]
```

### 7.5 Padrão Arquitetural Adotado

**Event-Driven Microservices** com CQRS — foi escolhido sobre as alternativas:

| Alternativa | Prós | Contras | Decisão |
|---|---|---|---|
| Monolito | Simples de operar | Não atende NFR-01 (coupling total) | Descartado |
| Microsserviços síncronos (REST-to-REST) | Simples | Transactions cai se Consolidation cair | Descartado |
| **Microsserviços + EDA (escolhido)** | Desacoplamento total | Consistência eventual | **Adotado** |
| Serverless (Lambda/Functions) | Escala zero | Latência fria; vendor lock-in | Descartado para v1 |

---

## 8. Modelo de Domínio

### 8.1 Transaction (Contexto: Lançamentos)

```mermaid
classDiagram
    class Transaction {
        +Guid Id
        +decimal Amount
        +TransactionType Type
        +string? Description
        +DateTime OccurredAt
        +DateTime CreatedAt
        +Create(decimal, TransactionType, string?, DateTime?) Transaction$
    }
    class TransactionType {
        <<enumeration>>
        Debit = 0
        Credit = 1
    }
    Transaction --> TransactionType
```

**Invariantes do domínio:**
- `Amount` deve ser estritamente positivo (`> 0`)
- `Type` deve ser `Debit` ou `Credit`
- `OccurredAt` é convertido para UTC antes de persistir

### 8.2 DailyConsolidation (Contexto: Consolidação)

```mermaid
classDiagram
    class DailyConsolidation {
        +Guid Id
        +DateOnly Date
        +decimal TotalCredits
        +decimal TotalDebits
        +decimal Balance
        +int TransactionCount
        +DateTime LastUpdatedAt
        +uint Version
        +Create(DateOnly) DailyConsolidation$
        +ApplyCredit(decimal) void
        +ApplyDebit(decimal) void
    }
```

**Invariantes do domínio:**
- `Date` é único por registro (índice único no banco)
- `Balance` é derivado: sempre `TotalCredits - TotalDebits`
- Apenas `ApplyCredit`/`ApplyDebit` modificam o estado (encapsulamento)
- `ApplyCredit`/`ApplyDebit` rejeitam valores `<= 0` com `ArgumentException`
- `Version` é o token de concorrência otimista, mapeado para a coluna de sistema `xmin` do PostgreSQL; é gerenciado pelo banco e nunca alterado pela aplicação

### 8.3 Contrato de Evento Compartilhado

```mermaid
classDiagram
    class TransactionCreatedEvent {
        <<record>>
        +Guid TransactionId
        +decimal Amount
        +string Type
        +DateTime OccurredAt
        +string? Description
    }
```

---

## 9. Design de APIs

### 9.1 Transactions API — Contratos

#### POST /api/v1/transactions

**Request Body:**
```json
{
  "amount": 1500.00,
  "type": "credit",
  "description": "Venda balcão - NF 1234",
  "occurredAt": "2026-10-19T10:30:00Z"
}
```

| Campo | Tipo | Obrigatório | Validação |
|---|---|---|---|
| `amount` | decimal | Sim | > 0 |
| `type` | string | Sim | "debit" ou "credit" (case-insensitive) |
| `description` | string | Não | — |
| `occurredAt` | datetime | Não | ISO 8601 UTC; default = now() |

**Response 201:**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "amount": 1500.00,
  "type": "credit",
  "occurredAt": "2026-10-19T10:30:00Z"
}
```

> O campo `type` da resposta ecoa o valor enviado na requisição (ex.: `"CREDIT"` retorna `"CREDIT"`); o valor persistido e publicado no evento é sempre normalizado.

**Response 400 — Exemplos:**
```json
{
  "error": "Amount must be positive. (Parameter 'amount')"
}
```
```json
{
  "error": "Invalid transaction type: 'transfer'. Use 'debit' or 'credit'."
}
```

---

#### GET /api/v1/transactions

**Query Parameters:**

| Parâmetro | Tipo | Obrigatório | Exemplo |
|---|---|---|---|
| `date` | DateOnly | Não | `2026-10-19` |

**Response 200:**
```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "amount": 1500.00,
    "type": "credit",
    "description": "Venda balcão",
    "occurredAt": "2026-10-19T10:30:00Z"
  }
]
```

---

### 9.2 Consolidation API — Contratos

#### GET /api/v1/consolidation/{date}

**Path Parameter:** `date` no formato `YYYY-MM-DD`

**Response 200:**
```json
{
  "date": "2026-10-19",
  "totalCredits": 3500.00,
  "totalDebits": 1200.00,
  "balance": 2300.00,
  "transactionCount": 5,
  "lastUpdatedAt": "2026-10-19T15:45:22Z"
}
```

**Response 404:**
```json
{
  "error": "No consolidation found for 2026-10-19"
}
```

---

### 9.3 Health Check

**Disponível em ambos os serviços:** `GET /health`

**Response 200** (`Content-Type: text/plain`, response writer padrão do ASP.NET Core):
```
Healthy
```

Se qualquer check falhar, retorna `503` com corpo `Unhealthy` (ou `Degraded`). O detalhamento por check não é exposto; os checks executados estão em [13.4](#134-health-checks).

---

## 10. Estratégia de Integração e Comunicação

### 10.1 Fluxo de Criação de Lançamento (Happy Path)

```mermaid
sequenceDiagram
    participant C as Cliente
    participant TC as TransactionsController
    participant H as CreateTransactionHandler
    participant DB as PostgreSQL (Transactions)
    participant MQ as RabbitMQ
    participant CN as TransactionCreatedConsumer
    participant DB2 as PostgreSQL (Consolidation)

    C->>TC: POST /api/v1/transactions
    TC->>H: mediator.Send(CreateTransactionCommand)
    H->>H: Transaction.Create() — valida invariantes
    H->>DB: db.SaveChangesAsync()
    DB-->>H: COMMIT
    H->>MQ: publishEndpoint.Publish(TransactionCreatedEvent)
    MQ-->>H: enfileirado
    H-->>TC: CreateTransactionResult
    TC-->>C: 201 Created

    Note over MQ,DB2: Processamento assíncrono — consistência eventual
    MQ->>CN: Entrega TransactionCreatedEvent
    CN->>DB2: Busca ou cria DailyConsolidation por data
    CN->>DB2: ApplyCredit() ou ApplyDebit() + SaveChangesAsync()
    alt Sem conflito
        DB2-->>CN: OK (xmin atualizado)
    else Conflito (xmin mudou ou insert duplicado do dia)
        DB2-->>CN: DbUpdateException
        CN->>CN: ChangeTracker.Clear() e relança
        Note over MQ,CN: Retry MassTransit reentrega a mensagem e o consumer relê o estado atual
    end
```

### 10.1.1 Concorrência no Consolidado

Todos os lançamentos de um mesmo dia atualizam **a mesma linha** de `DailyConsolidations`, num ciclo ler → modificar → gravar. Sem controle, dois consumers simultâneos leem o mesmo saldo e o último a gravar sobrescreve o outro (lost update), ou ambos tentam criar o registro de um dia novo e um falha no índice único.

A solução combina três mecanismos (decisão em [ADR-009](#adr-009-controle-de-concorrência-no-consolidado)):

| Mecanismo | Onde | Efeito |
|---|---|---|
| **Optimistic locking (`xmin`)** | `ConsolidationDbContext` — `Version` com `IsRowVersion()` | `UPDATE ... WHERE "xmin" = @original`; se outra transação alterou a linha, 0 linhas afetadas → `DbUpdateConcurrencyException` |
| **Retry com backoff exponencial** | `Program.cs` — `UseMessageRetry` | Conflitos (concorrência ou insert duplicado) são reprocessados com estado atualizado em vez de irem para a fila de erro |
| **Consumo serial por instância** | `Program.cs` — `ConcurrentMessageLimit = 1` | Elimina conflitos dentro de uma instância; o optimistic locking + retry cobrem conflitos entre instâncias |

**Validação (testes de carga, 2026-10-18):**

| Cenário | Resultado |
|---|---|
| 40 lançamentos simultâneos no mesmo dia, sem controle de concorrência | Lançamentos perdidos (mensagens na fila de erro, saldo incorreto) |
| 100 lançamentos simultâneos, 1 instância | 100/100 consolidados corretamente |
| 150 lançamentos simultâneos, 3 instâncias do consumer | 150/150 corretos; 27 conflitos detectados e resolvidos via retry |

### 10.2 Fluxo de Consulta de Saldo (Cache Hit vs Miss)

```mermaid
sequenceDiagram
    participant C as Cliente
    participant CC as ConsolidationController
    participant H as GetDailyBalanceHandler
    participant R as Redis
    participant DB as PostgreSQL (Consolidation)

    C->>CC: GET /api/v1/consolidation/2026-10-19
    CC->>H: mediator.Send(GetDailyBalanceQuery)
    H->>R: GetStringAsync("consolidation:2026-10-19")

    alt Cache HIT
        R-->>H: DailyBalanceDto serializado
        Note over H: retorno em menos de 1ms
    else Cache MISS
        R-->>H: null
        H->>DB: FirstOrDefaultAsync(date)
        DB-->>H: DailyConsolidation
        H->>R: SetStringAsync(key, dto, TTL 60s)
    end

    H-->>CC: DailyBalanceDto
    CC-->>C: 200 OK
```

### 10.3 Configuração do RabbitMQ (MassTransit)

**Topologia (criada automaticamente pelo MassTransit):**

| Elemento | Nome | Tipo |
|---|---|---|
| Exchange do evento | `CashFlow.Shared.Events:TransactionCreatedEvent` | Fan-out |
| Exchange/queue do consumer | `TransactionCreated` | Fan-out → queue durável |
| Fila de erro | `TransactionCreated_error` | Queue durável |

- **Queue durável:** Sim (`durable: true`)
- **Retry policy:** `UseMessageRetry` no nível do bus — até 10 tentativas com backoff exponencial (50ms → 5s, incremento de 100ms). `ArgumentException` (erros de validação) é ignorada pelo retry
- **Fila de erro:** mensagens que esgotam o retry ou lançam exceção ignorada são movidas pelo MassTransit para `TransactionCreated_error` (não é um DLX do RabbitMQ). Reprocessamento é manual — ex.: mover as mensagens de volta para `TransactionCreated` pela Management UI
- **Concorrência do consumer:** `ConcurrentMessageLimit = 1` — cada instância processa uma mensagem por vez (ver [10.1.1](#1011-concorrência-no-consolidado))

---

## 11. Modelo de Dados

### 11.1 Schema: cashflow_transactions

```sql
CREATE TABLE "Transactions" (
    "Id"          UUID            NOT NULL PRIMARY KEY,
    "Amount"      NUMERIC(18, 2)  NOT NULL,
    "Type"        TEXT            NOT NULL,  -- 'Debit' ou 'Credit' (enum convertido para string)
    "Description" TEXT,
    "OccurredAt"  TIMESTAMPTZ     NOT NULL,
    "CreatedAt"   TIMESTAMPTZ     NOT NULL
);

CREATE INDEX "IX_Transactions_OccurredAt"
    ON "Transactions" ("OccurredAt");
```

**Justificativa do índice:** Consultas por data (`GET /transactions?date=`) filtram por `OccurredAt`. Sem índice, full table scan em volumes altos.

---

### 11.2 Schema: cashflow_consolidation

```sql
CREATE TABLE "DailyConsolidations" (
    "Id"               UUID            NOT NULL PRIMARY KEY,
    "Date"             DATE            NOT NULL,
    "TotalCredits"     NUMERIC(18, 2)  NOT NULL,
    "TotalDebits"      NUMERIC(18, 2)  NOT NULL,
    "TransactionCount" INTEGER         NOT NULL,
    "LastUpdatedAt"    TIMESTAMPTZ     NOT NULL
    -- "xmin" (coluna de sistema do PostgreSQL) usada como token de concorrência
);

CREATE UNIQUE INDEX "IX_DailyConsolidations_Date"
    ON "DailyConsolidations" ("Date");
```

**Justificativa do índice único:** Garante que exista no máximo um consolidado por data. O índice único é um guard de integridade além da lógica de aplicação; quando dois consumers criam o mesmo dia simultaneamente, a violação do índice é tratada como conflito e reprocessada via retry.

**Token de concorrência (`xmin`):** toda linha do PostgreSQL possui a coluna de sistema `xmin`, que muda a cada `UPDATE`. A propriedade `Version` da entidade é mapeada para ela, então o controle de concorrência não exige coluna adicional. O mapeamento de `xmin` é registrado no modelo do EF Core mas não gera DDL.

---

### 11.3 Estrutura do Cache Redis

| Chave | Valor | TTL |
|---|---|---|
| `consolidation:2026-10-19` | JSON serializado de `DailyBalanceDto` | 60 segundos |

**Consideração de TTL:** 60 segundos é um trade-off entre consistência (saldo pode estar até 1min desatualizado) e performance (elimina carga no PostgreSQL). Para o dia atual, pode-se considerar TTL menor (ex: 5s) em uma evolução futura.

> **Limitação atual:** o consumer não invalida a chave do cache ao atualizar o consolidado. A atualização só fica visível após a expiração do TTL. Ver roadmap em [19.1](#191-curto-prazo-1-3-meses).

---

## 12. Estratégia de Segurança

### 12.1 Controles Implementados (v1.0)

| Controle | Status | Descrição |
|---|---|---|
| **Autenticação JWT (Keycloak)** | Implementado | Todos os endpoints protegidos com Bearer JWT validado localmente via JWKS |
| **Autorização por Role** | Implementado | Roles granulares por operação: `transactions:write`, `transactions:read`, `consolidation:read` |
| HTTPS/TLS | Delegado ao infra | Terminação TLS no Load Balancer |
| Validação de input | Implementado | Validação em domínio + controller |
| SQL Injection | Mitigado | EF Core com consultas parametrizadas |
| Dados sensíveis | N/A | Não há PII nesta versão |

### 12.2 Modelo de Autenticação e Autorização

**Identity Provider:** Keycloak 26 (Realm: `cashflow`, Client: `cashflow-api`)

**Fluxo de autenticação:**

```mermaid
sequenceDiagram
    participant C as Cliente
    participant KC as Keycloak :9080
    participant API as Transactions / Consolidation API

    Note over API,KC: Inicialização — uma vez
    API->>KC: GET /realms/cashflow/.well-known/openid-configuration
    KC-->>API: JWKS URI + metadados do realm
    API->>KC: GET /realms/cashflow/protocol/openid-connect/certs
    KC-->>API: Chaves públicas RS256 (JWKS)

    Note over C,KC: Por sessão do usuário
    C->>KC: POST /realms/cashflow/protocol/openid-connect/token\ngrant_type=password · client_id · client_secret · username · password
    KC-->>C: access_token (JWT) · refresh_token · expires_in

    Note over C,API: Por requisição
    C->>API: GET/POST endpoint\nAuthorization: Bearer eyJ...
    Note over API: Valida assinatura JWT com JWKS local\nVerifica exp · aud · iss\nExtrai claim "roles"
    API-->>C: 200 OK / 401 Unauthorized / 403 Forbidden
```

**Roles e endpoints protegidos:**

| Role | Serviço | Endpoint | Método |
|---|---|---|---|
| `transactions:write` | Transactions API | `/api/v1/transactions` | POST |
| `transactions:read` | Transactions API | `/api/v1/transactions` | GET |
| `consolidation:read` | Consolidation API | `/api/v1/consolidation/{date}` | GET |

**Usuários pré-configurados (desenvolvimento):**

| Usuário | Senha | Roles |
|---|---|---|
| `cashflow-admin` | `admin123` | `transactions:write`, `transactions:read`, `consolidation:read` |
| `cashflow-reader` | `reader123` | `transactions:read`, `consolidation:read` |

**Detalhes de configuração JWT:**
- Authority Docker: `http://keycloak:8080/realms/cashflow`
- Authority local: `http://localhost:9080/realms/cashflow`
- Audience: `cashflow-api` — incluída no token pelo protocol mapper `cashflow-api-audience` (`oidc-audience-mapper`) do client
- Algoritmo: RS256 (chaves gerenciadas pelo Keycloak)
- Claim de role: `roles` (mapeado via protocol mapper `realm-roles` do Keycloak)
- `MapInboundClaims = false`: sem isso, o handler JWT do ASP.NET Core renomeia `roles` para a URI `ClaimTypes.Role` e a checagem `RoleClaimType = "roles"` nunca encontra os papéis (todo request autenticado retornaria 403)
- Validação: assinatura + `exp` + `aud` + `iss` (sem round-trip ao IdP por request)

**Issuer em Docker:** o cliente obtém o token em `localhost:9080`, então o `iss` do token é `http://localhost:9080/realms/cashflow`, enquanto a API, dentro da rede Docker, busca os metadados em `http://keycloak:8080`. Por isso a configuração `Keycloak:ValidIssuers` aceita os dois issuers.

| Configuração | Variável de ambiente (Docker Compose) |
|---|---|
| `Keycloak:Authority` | `Keycloak__Authority=http://keycloak:8080/realms/cashflow` |
| `Keycloak:ValidIssuers` | `Keycloak__ValidIssuers=http://localhost:9080/realms/cashflow,http://keycloak:8080/realms/cashflow` |

### 12.3 Controles Recomendados (roadmap)

**Credenciais do RabbitMQ por serviço:**
- Transactions: permissão apenas de publicação (`publish`)
- Consolidation: permissão apenas de consumo (`consume`)
- TLS no canal AMQP em produção

**Secrets Management:**
- Connection strings e client secret via AWS Secrets Manager ou Azure Key Vault
- Nunca em variáveis de ambiente em texto plano em produção

**Rate Limiting:**
- Proteção contra abuso na API de lançamentos
- Exemplo: 1000 req/min por IP; 100 req/min por token autenticado

**Auditoria:**
- Log de criações de lançamento com IP de origem e `sub` (usuário) do JWT
- Retenção de logs por 90 dias (mínimo)

---

## 13. Estratégia de Resiliência

### 13.1 Resiliência do Serviço de Lançamentos

O isolamento em relação ao serviço de Consolidação é garantido por design:

```mermaid
flowchart TD
    Client(["Cliente"])
    TX["Transactions API\nONLINE"]
    PG[("PostgreSQL\nTransactions")]
    MQ[("RabbitMQ\nfila durável")]
    CONS["Consolidation API\nOFFLINE"]

    Client -->|"POST /transactions\nRetorna 201 — sucesso"| TX
    TX -->|"1. Persiste lançamento"| PG
    TX -->|"2. Publica evento"| MQ
    MQ -.->|"mensagem aguarda\naté Consolidation voltar"| CONS
    CONS -.->|"quando voltar online\nprocessa fila pendente"| MQ

    style CONS fill:#ffcccc,stroke:#cc0000
    style TX fill:#ccffcc,stroke:#006600
```

### 13.2 Retry Policy (MassTransit)

```csharp
cfg.UseMessageRetry(r =>
{
    r.Exponential(10, TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100));
    r.Ignore<ArgumentException>();
});
```

```
Tentativa 1 → falha → backoff exponencial (50ms … 5s) → … → Tentativa 10 → TransactionCreated_error
```

- **Falhas transitórias** (conflito de concorrência, banco momentaneamente indisponível): até 10 tentativas, com intervalos curtos no início porque o caso mais comum é conflito de concorrência, resolvido em milissegundos
- **Falhas de validação** (`ArgumentException`): não há retry, a mensagem vai direto para `TransactionCreated_error`, porque repetir não mudaria o resultado
- Mensagens na fila de erro exigem análise e reprocessamento manual

### 13.3 Resiliência do Consolidation em Picos

```
50 RPS chegando em GET /consolidation/{date}
│
├─ ~95% das requisições: CACHE HIT no Redis (< 1ms)
│   └── Redis suporta > 100.000 RPS por instância
│
└─ ~5% das requisições: CACHE MISS → PostgreSQL
    └── PostgreSQL com índice único por Date: < 10ms
```

**Resultado:** Nenhuma requisição é perdida em condições normais de pico. O Redis absorve a carga.

### 13.4 Health Checks

Ambos os serviços expõem `GET /health` que verifica:

| Serviço | Checks |
|---|---|
| Transactions API | PostgreSQL (transactions), bus MassTransit/RabbitMQ (registrado automaticamente pelo MassTransit) |
| Consolidation API | PostgreSQL (consolidation), Redis, bus MassTransit/RabbitMQ (registrado automaticamente) |

### 13.5 Tratamento de Erros da Aplicação

#### Global Exception Handler

Ambos os serviços possuem um middleware de tratamento global de exceções registrado como **primeiro middleware no pipeline** (antes do Swagger e autenticação). Garante que qualquer exceção não tratada resulte em:

- Resposta JSON estruturada com `error` e `traceId` (jamais stack trace exposto ao cliente)
- Log de `Error` com o `TraceId`, método HTTP e path — rastreável no sistema de logs
- HTTP 500 com `Content-Type: application/json`

```json
{
  "error": "An unexpected error occurred.",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

#### Resiliência do Cache Redis (Consolidation)

A leitura e escrita no Redis estão isoladas em métodos com try-catch. Redis fora do ar não derruba o endpoint de consulta:

| Cenário | Comportamento | Log |
|---|---|---|
| Redis indisponível na leitura | Fallback transparente para PostgreSQL | `Warning` com exceção |
| Redis indisponível na escrita | Resultado retornado sem cachear | `Warning` com exceção |
| Redis disponível | Fluxo normal de cache-aside | `Debug` (hit ou miss) |

#### Validação no Consumer (Fila de Erro)

O `TransactionCreatedConsumer` valida explicitamente o tipo do evento antes de processar. Um evento com tipo desconhecido:
1. Loga `Error` com o `TransactionId` e tipo inválido
2. Lança `ArgumentException`, que é ignorada pelo retry policy
3. A mensagem é movida imediatamente para `TransactionCreated_error`

Isso evita que uma mensagem malformada entre em loop infinito silencioso ou atualize dados incorretamente.

#### Conflitos de Concorrência no Consumer

Quando `SaveChangesAsync` lança `DbUpdateException` (conflito de `xmin` ou violação do índice único de `Date`), o consumer:
1. Loga `Warning` com a data e o `TransactionId`
2. Executa `ChangeTracker.Clear()` para descartar entidades rastreadas com estado obsoleto
3. Relança a exceção, e o retry policy reentrega a mensagem, que relê o consolidado atualizado

Como o `SaveChanges` que falhou não persistiu nada, o reprocessamento não duplica valores.

#### Validação de Domínio (DailyConsolidation)

`ApplyCredit` e `ApplyDebit` rejeitam valores `<= 0` com `ArgumentException`. A validação no domínio garante que regras de negócio críticas sejam aplicadas independentemente de qual camada invocou o método.

---

## 14. Observabilidade e Monitoramento

### 14.1 Logging Estruturado

Framework: `ILogger<T>` nativo do ASP.NET Core com saída JSON em produção.

**Eventos de negócio logados:**

| Evento | Nível | Serviço | Campos |
|---|---|---|---|
| Lançamento persistido no banco | `Information` | Transactions | TransactionId, Amount, Type, OccurredAt |
| Evento consumido pelo consolidation | `Information` | Consolidation | TransactionId, Type, Amount, Date |
| Consolidação atualizada | `Information` | Consolidation | Date, Balance, TransactionCount |
| Cache hit | `Debug` | Consolidation | CacheKey |
| Cache miss | `Debug` | Consolidation | CacheKey |
| Erro de validação de request | `Warning` | Transactions | Mensagem de erro, Amount, Type |
| Falha ao publicar evento no RabbitMQ | `Error` | Transactions | TransactionId, Exceção |
| Tipo de evento desconhecido no consumer | `Error` | Consolidation | TransactionId, Type (vai para a fila de erro) |
| Conflito de concorrência no consolidado | `Warning` | Consolidation | Date, TransactionId, Exceção (será reprocessado via retry) |
| Falha de leitura no Redis | `Warning` | Consolidation | CacheKey, Exceção |
| Falha de escrita no Redis | `Warning` | Consolidation | CacheKey, Exceção |
| Exceção não tratada (global handler) | `Error` | Ambos | Method, Path, TraceId, Exceção |

### 14.2 Métricas (Roadmap — OpenTelemetry)

```
Métricas recomendadas:
│
├── cashflow_transactions_created_total        (counter)
├── cashflow_transactions_created_amount_sum   (counter - valor acumulado)
├── cashflow_http_request_duration_seconds     (histogram - p50, p95, p99)
├── cashflow_consolidation_cache_hit_total     (counter)
├── cashflow_consolidation_cache_miss_total    (counter)
└── cashflow_rabbitmq_messages_processed_total (counter)
```

### 14.3 Alertas Recomendados

| Alerta | Condição | Severidade |
|---|---|---|
| Health check falhou | `/health` retorna não-Healthy por > 30s | Critical |
| Taxa de erro alta | `5xx / total > 1%` em 5 minutos | High |
| Latência elevada | `p95 > 500ms` em Transactions por 5 min | Medium |
| Cache hit rate baixo | `hit_rate < 80%` no Consolidation | Low |
| Fila de erro com mensagens | `TransactionCreated_error` com > 0 mensagens por > 1 hora | High |
| Conflitos de concorrência frequentes | Taxa de logs "Concurrency conflict" alta e sustentada | Low (indica contenção — avaliar ADR-009) |

### 14.4 Tracing Distribuído (Roadmap)

Com OpenTelemetry, cada requisição geraria um `TraceId` propagado através de:
1. Header HTTP `traceparent`
2. Header AMQP no corpo da mensagem RabbitMQ

Permitiria visualizar o path completo: `POST /transactions → RabbitMQ → Consumer → PostgreSQL` em ferramentas como Jaeger ou AWS X-Ray.

---

## 15. Arquitetura de Deploy

### 15.1 Docker Compose (Desenvolvimento Local)

```yaml
Serviços:
├── keycloak           → :9080  (Admin UI: /admin · Token: /realms/cashflow/protocol/openid-connect/token)
├── transactions-api   → :8080
├── consolidation-api  → :8081
├── postgres-transactions   → :5432
├── postgres-consolidation  → :5433
├── rabbitmq           → :5672 (AMQP), :15672 (Management)
└── redis              → :6379
```

**Auto-provisioning:** Migrations aplicadas automaticamente no startup (`db.Database.Migrate()`). O realm do Keycloak é importado de `keycloak/cashflow-realm.json` apenas na criação do container; mudanças no arquivo exigem recriar o container (`docker compose up -d --force-recreate keycloak`).

**Configuração das APIs no Docker:** os `appsettings.json` apontam para `localhost` (execução local). No Docker Compose, os endereços são sobrescritos por variáveis de ambiente no `docker-compose.yml` (`ConnectionStrings__Default`, `RabbitMQ__Host`, `Redis__ConnectionString`, `Keycloak__Authority`, `Keycloak__ValidIssuers`), que apontam para os nomes dos serviços na rede Docker. Arquivos `appsettings.Docker.json` estão no `.gitignore` e não devem ser usados para essa configuração.

> **API Gateway em desenvolvimento:** o AWS API Gateway não existe no ambiente local. Os serviços são acessados diretamente em `:8080` (Transactions) e `:8081` (Consolidation). A validação de JWT pelo Keycloak continua funcionando via JwtBearer em cada serviço.

### 15.2 Arquitetura AWS (Produção)

```mermaid
flowchart TD
    Internet(["Internet"])

    subgraph AWS["AWS Cloud"]
        APIGW["AWS API Gateway\nHTTP API\nJWT Authorizer · Throttling\n500 RPS burst limit"]
        KC_PROD["Keycloak\nIdentity Provider\n(EKS ou Keycloak Cloud)"]
        ALB["Application Load Balancer\ninterno · VPC Link"]

        subgraph EKS["EKS Cluster"]
            subgraph NS_TX["Deployment: transactions\nHPA · Min 2 · Max 10 pods"]
                TX1["transactions-api\nPod · 0.5 vCPU · 1GB"]
                TX2["transactions-api\nPod · 0.5 vCPU · 1GB"]
            end

            subgraph NS_CONS["Deployment: consolidation\nHPA · Min 2 · Max 10 pods"]
                CN1["consolidation-api\nPod · 0.5 vCPU · 1GB"]
                CN2["consolidation-api\nPod · 0.5 vCPU · 1GB"]
            end
        end

        RDS_TX[("RDS PostgreSQL\ndb.t3.small · Multi-AZ\ncashflow_transactions")]
        RDS_CONS[("RDS PostgreSQL\ndb.t3.small · Multi-AZ\ncashflow_consolidation")]
        AQ[("Amazon MQ\nRabbitMQ · mq.m5.large")]
        EC[("ElastiCache Redis\ncache.t3.micro")]
    end

    Internet --> APIGW
    APIGW -.->|"valida JWT"| KC_PROD
    APIGW -->|"VPC Link"| ALB
    ALB -->|"/transactions"| NS_TX
    ALB -->|"/consolidation"| NS_CONS
    NS_TX --> RDS_TX
    NS_TX --> AQ
    AQ --> NS_CONS
    NS_CONS --> RDS_CONS
    NS_CONS <--> EC
```

---

## 16. Architecture Decision Records (ADRs)

### ADR-001: Microsserviços em vez de Monolito

**Contexto:** O requisito NFR-01 exige que o serviço de lançamentos permaneça operacional mesmo quando o consolidado falha.

**Decisão:** Dois microsserviços independentes com deploys separados.

**Consequências:**
- (+) Isolamento de falhas — falha no Consolidation não afeta Transactions
- (+) Escalabilidade independente — cada serviço escala conforme sua carga
- (+) Deploy independente — atualizações sem downtime do outro serviço
- (-) Complexidade operacional maior (2 apps, 2 bancos, mensageria)
- (-) Consistência eventual (trade-off aceito pelo negócio)

---

### ADR-002: RabbitMQ como Message Broker

**Contexto:** Precisamos de comunicação assíncrona entre os serviços.

**Decisão:** RabbitMQ via MassTransit.

**Alternativas consideradas:**

| Opção | Prós | Contras |
|---|---|---|
| **RabbitMQ + MassTransit** | Maturidade, retry built-in, DLQ automática | Não é Kafka (sem replay de histórico) |
| Apache Kafka | Replay de eventos, log imutável | Over-engineering para volume atual |
| AWS SQS | Serverless, gerenciado | Vendor lock-in; polling model |

**Justificativa:** RabbitMQ é suficiente para o volume (50 RPS) e MassTransit abstrai retry, DLQ e serialização. Kafka seria over-engineering aqui.

---

### ADR-003: CQRS com MediatR

**Contexto:** Commands (escrita) e Queries (leitura) têm características e otimizações distintas.

**Decisão:** MediatR para separação de Commands e Queries dentro de cada serviço.

**Consequências:**
- (+) Cada handler tem responsabilidade única e é testável isoladamente
- (+) Facilita evolução: otimizar leitura (ex: read replica) sem afetar escrita
- (+) Desacopla controller da lógica de negócio
- (-) Mais classes/arquivos que uma abordagem direta

---

### ADR-004: Cache-Aside com Redis (TTL 60s)

**Contexto:** Consolidation precisa suportar 50 RPS com ≤ 5% de perda sem sobrecarregar o PostgreSQL.

**Decisão:** Cache-aside com Redis e TTL de 60 segundos.

**Cálculo:** Com 50 RPS e TTL 60s, o PostgreSQL recebe no máximo 1 query a cada 60s por data consultada (cache miss) mais um volume mínimo de cache misses aleatórios. Isso é praticamente zero carga comparado a 50 RPS direto no banco.

**Trade-off de TTL:** 60 segundos = saldo pode estar até 1 minuto desatualizado. Aceitável para relatório de saldo diário consolidado (não é dado em tempo real crítico).

---

### ADR-005: Database per Service

**Contexto:** Isolamento de dados entre os microsserviços.

**Decisão:** Dois bancos PostgreSQL separados — um por serviço.

**Consequências:**
- (+) Falha em um banco não derruba o outro serviço
- (+) Schema pode evoluir independentemente
- (+) Cada banco pode usar tecnologia diferente no futuro
- (-) Não há joins cross-service (resolvido via eventos)
- (-) Custo de dois bancos vs um

---

### ADR-006: Consistência Eventual como Modelo de Consistência

**Contexto:** Com comunicação assíncrona, o consolidado pode ficar alguns segundos atrás dos lançamentos.

**Decisão:** Aceitar consistência eventual entre Transactions e Consolidation.

**Justificativa:** O requisito de negócio é "relatório de saldo diário consolidado" — não "saldo em tempo real". Um delay de segundos é imperceptível e aceitável para o comerciante. Alternativas como Saga 2-phase commit adicionariam complexidade desproporcional ao benefício.

---

### ADR-007: Keycloak como Identity Provider

**Contexto:** Os endpoints das APIs precisam de autenticação e autorização com controle granular por operação. A solução deve ser portável, baseada em padrões abertos e versionável como código.

**Decisão:** Keycloak 26 como Identity Provider com autenticação JWT Bearer em cada serviço e realm exportável em JSON.

**Alternativas consideradas:**

| Opção | Prós | Contras |
|---|---|---|
| **Keycloak (escolhido)** | Open source; OIDC/OAuth2 padrão; realm como código; fácil de dockerizar | Maior consumo de memória (~512MB) |
| ASP.NET Core Identity | Nativo; sem infra adicional | Não é federado; dificulta SSO e multitenancy |
| AWS Cognito | Gerenciado; sem infra | Vendor lock-in; custo por MAU em produção |
| Auth0 | Gerenciado; fácil integração | Vendor lock-in; custo por MAU |

**Consequências:**
- (+) Padrão OAuth2/OIDC — compatível com qualquer cliente (browser, mobile, M2M)
- (+) Roles e scopes configuráveis por realm sem alterar código de aplicação
- (+) Tokens JWT validados localmente (sem round-trip ao IdP por request)
- (+) Realm exportado em JSON — configuração versionada como código (`keycloak/cashflow-realm.json`)
- (+) Realm importado automaticamente no startup do container via `--import-realm`
- (-) Serviço adicional no docker-compose (~512MB RAM)
- (-) Tempo de startup maior (~60–90s em desenvolvimento)

---

### ADR-008: AWS API Gateway HTTP API como ponto de entrada

**Contexto:** Com dois microsserviços expostos, é necessário um ponto de entrada único que centralize roteamento, throttling e validação de JWT — sem replicar essa lógica em cada serviço individualmente.

**Decisão:** AWS API Gateway HTTP API com JWT Authorizer apontando para o Keycloak, integração privada via VPC Link com o ALB interno do EKS.

**Alternativas consideradas:**

| Opção | Prós | Contras |
|---|---|---|
| **AWS API Gateway HTTP API (escolhido)** | Gerenciado; zero infra; JWT Authorizer nativo; throttling built-in | Vendor lock-in; não roda em dev; latência adicional ~5ms |
| Traefik | Funciona em dev e prod; discovery automático | Infra adicional para gerenciar em prod |
| Kong | Rico em plugins; admin API | Mais complexo; requer banco próprio |
| NGINX Ingress | Simples; padrão K8s | Sem admin API; recursos de API management limitados |

**Consequências:**
- (+) Ponto de entrada único — clientes não conhecem endereços internos dos serviços
- (+) JWT Authorizer centraliza validação de token antes de chegar nas APIs (primeira camada de defesa)
- (+) Throttling e burst limit configuráveis por rota sem alterar código
- (+) Zero infra para gerenciar em produção
- (+) Custo por uso — ~$1/milhão de requests (baixo custo para volumes pequenos)
- (-) Não existe em desenvolvimento local; ambiente assimétrico entre dev e prod
- (-) Latência adicional de ~5ms por request
- (-) Vendor lock-in AWS

**Nota sobre dupla validação de JWT:** o API Gateway valida o token na borda (latência mínima, rejeição rápida). Os serviços também validam localmente via JwtBearer — defesa em profundidade; nenhuma configuração precisa ser removida.

---

### ADR-009: Controle de Concorrência no Consolidado

**Contexto:** Cada lançamento atualiza a linha do seu dia em `DailyConsolidations` (ler → modificar → gravar). Como praticamente todos os lançamentos caem no dia corrente, essa linha é um *hot row*. Testes de carga mostraram que, sem controle, lançamentos simultâneos eram perdidos: inserts concorrentes do mesmo dia violavam o índice único e iam para a fila de erro, e updates concorrentes sobrescreviam uns aos outros.

**Decisão:** Optimistic locking com a coluna de sistema `xmin` do PostgreSQL + retry com backoff exponencial no MassTransit + `ConcurrentMessageLimit = 1` por instância do consumer.

**Alternativas consideradas:**

| Opção | Prós | Contras |
|---|---|---|
| **Optimistic locking + retry + consumo serial (escolhido)** | Mantém a lógica no domínio (`ApplyCredit`/`ApplyDebit`); correto com N instâncias; sem coluna extra | Retries sob alta contenção entre instâncias; throughput por instância limitado ao processamento serial |
| Apenas retry com paralelismo padrão (16) | Maior paralelismo | Testado: retries simultâneos colidem de novo e esgotam as tentativas |
| Upsert atômico em SQL (`INSERT ... ON CONFLICT DO UPDATE SET Total = Total + x`) | Sem conflitos; máximo throughput | Regra de negócio sai do domínio para SQL; acoplamento ao PostgreSQL |
| Lock pessimista (`SELECT ... FOR UPDATE`) | Sem retries | Transações explícitas; bloqueios e risco de deadlock |
| Single active consumer (RabbitMQ) | Serialização global | Escalabilidade horizontal do consumer anulada |

**Consequências:**
- (+) Nenhum lançamento perdido ou sobrescrito, validado com 150 lançamentos simultâneos em 3 instâncias
- (+) Sem alteração de schema (`xmin` já existe em toda tabela PostgreSQL)
- (+) Regras de negócio permanecem na entidade de domínio
- (-) Throughput por instância limitado pelo processamento serial (suficiente para o volume atual: cada mensagem leva poucos ms)
- (-) Com muitas instâncias, a contenção no *hot row* gera mais retries. Se isso virar gargalo, migrar para upsert atômico

---

## 17. Arquitetura de Transição

### 17.1 Cenário de Migração de Legado

Assumindo que a organização possui um sistema legado monolítico (ex: planilha Excel, sistema desktop, ERP antigo) e precisa migrar para esta solução:

### 17.2 Fases de Transição

```mermaid
flowchart LR
    subgraph F1["Fase 1 — Meses 1-2\nStrangler Fig"]
        Legado1["Sistema Legado\nContinua ativo"]
        CF1["CashFlow\nNovos lançamentos"]
    end

    subgraph F2["Fase 2 — Mês 3\nMigração de Dados"]
        ETL["Script ETL\nextrai · transforma · carrega"]
        CF2["CashFlow\nImporta histórico\nConsolidações recalculadas"]
    end

    subgraph F3["Fase 3 — Mês 4\nDescomissionamento"]
        CF3["CashFlow\nFonte única de verdade"]
        Legado3["Sistema Legado\nDesativado"]
    end

    Legado1 -.->|"novos lançamentos\nvão para CashFlow"| CF1
    F1 -->|"ETL batch"| F2
    ETL -->|"dados históricos"| CF2
    F2 -->|"validação e cutover"| F3
    Legado3 -.->|"descontinuado"| CF3

    style Legado3 fill:#ffcccc,stroke:#cc0000
    style CF3 fill:#ccffcc,stroke:#006600
```

### 17.3 Estratégia de Migração de Dados

**Para cada lançamento histórico no legado:**

```json
POST /api/v1/transactions
{
  "amount": 500.00,
  "type": "credit",
  "description": "[MIGRADO] Venda 18/10/2026",
  "occurredAt": "2026-10-18T00:00:00Z"
}
```

O campo `occurredAt` permite importar datas históricas preservando a linha do tempo real. O consolidado é recalculado automaticamente via eventos.

---

## 18. Estimativa de Custos

### 18.1 Ambiente de Produção — AWS (Pequeno Porte)

| Componente | Serviço AWS | Configuração | Custo/mês (USD) |
|---|---|---|---|
| API Gateway | AWS API Gateway HTTP API | ~1M req/mês + VPC Link | ~$8 |
| EKS Control Plane | EKS | 1 cluster | ~$73 |
| EC2 Worker Nodes | EC2 t3.small | 2 nodes | ~$30 |
| PostgreSQL Transactions | RDS db.t3.micro Multi-AZ | 20GB SSD | ~$30 |
| PostgreSQL Consolidation | RDS db.t3.micro Multi-AZ | 20GB SSD | ~$30 |
| RabbitMQ | Amazon MQ mq.m5.large | Single-instance | ~$100 |
| Redis | ElastiCache cache.t3.micro | Single-node | ~$15 |
| Load Balancer | ALB (interno) | Por hora + LCUs | ~$20 |
| Transferência de dados | — | Estimativa baixo uso | ~$5 |
| **Total estimado** | | | **~$311/mês** |

### 18.2 Otimizações de Custo Possíveis

| Otimização | Economia Estimada | Trade-off |
|---|---|---|
| RDS Single-AZ (não Multi-AZ) | -$15/banco | Menor disponibilidade em falha de AZ |
| Amazon MQ broker.mq.t3.micro | -$80 | Limite de conexões; não recomendado para prod |
| EC2 Spot Instances (nodes) | -60% nos nodes | Pods podem ser interrompidos; usar PodDisruptionBudget |
| ElastiCache Serverless | Variável | Pay-per-use; pode ser mais caro em pico |

### 18.3 Custo para Escala (Médio Porte — 500 RPS)

| Componente | Ajuste | Custo adicional |
|---|---|---|
| EC2 Worker Nodes | 2 nodes adicionais (t3.small) | +$30/mês |
| RDS | db.t3.small Multi-AZ | +$20/banco |
| ElastiCache | cache.t3.small | +$20 |
| Amazon MQ | cluster (3 brokers) | +$200 |
| **Total estimado 500 RPS** | | **~$600/mês** |

---

## 19. Melhorias Futuras e Roadmap

### 19.1 Curto Prazo (1-3 meses)

| Melhoria | Impacto | Complexidade |
|---|---|---|
| **Idempotência no Consumer** | Evita duplicatas em caso de redelivery do RabbitMQ | Baixa |
| **Outbox Pattern** | Garante atomicidade entre persistência no DB e publish no RabbitMQ | Média |
| **Invalidação de cache no Consumer** | Saldo do dia atual atualizado em segundos, não 1 minuto | Baixa |

**Detalhe — Idempotência:** Adicionar tabela `ProcessedEvents` com `TransactionId` (unique) no banco do Consolidation, gravada na mesma transação do consolidado. Hoje, se o RabbitMQ reentregar uma mensagem já gravada (ex.: a instância cai após o commit e antes do ack), o lançamento é somado em dobro. Os retries de conflito de concorrência não causam esse problema, porque o `SaveChanges` que falhou não persiste nada.

**Detalhe — Invalidação de cache:** após o `SaveChanges` bem-sucedido, o consumer deve remover (ou atualizar) a chave `consolidation:{yyyy-MM-dd}` no Redis. Hoje o saldo consultado pode ficar até 60s desatualizado (ver [11.3](#113-estrutura-do-cache-redis)).

**Detalhe — Outbox Pattern:**
```
ATUAL:  db.SaveChanges() + publishEndpoint.Publish()  ← não atômico
FUTURO: db.SaveChanges() inclui OutboxMessage         ← atômico
        Background worker processa OutboxMessage e publica
```

### 19.2 Médio Prazo (3-6 meses)

| Melhoria | Impacto | Complexidade |
|---|---|---|
| **OpenTelemetry** | Observabilidade end-to-end (traces, métricas) | Média |
| **Read Replicas no PostgreSQL** | Separa carga de leitura e escrita | Alta |
| **Rate Limiting** | Proteção contra abuso | Baixa |
| **Paginação na listagem** | Performance em grandes volumes | Baixa |
| **Categorias de lançamento** | Análise por tipo (vendas, despesas, etc.) | Média |

### 19.3 Longo Prazo (6-12 meses)

| Melhoria | Impacto | Complexidade |
|---|---|---|
| **Relatórios avançados** | Exportação PDF/CSV, gráficos, análise de tendências | Alta |
| **Multi-tenant** | Suporte a múltiplos comerciantes com isolamento de dados | Alta |
| **Integração bancária** | Conciliação automática via Open Finance | Alta |
| **Event Sourcing** | Histórico completo de mudanças no consolidado | Alta |
| **Mobile App** | Interface nativa para iOS/Android | Alta |

---

## 20. Glossário

| Termo | Definição |
|---|---|
| **Lançamento** | Registro de um evento financeiro (entrada ou saída) |
| **Débito** | Saída de dinheiro (pagamento, despesa) |
| **Crédito** | Entrada de dinheiro (venda, recebimento) |
| **Saldo** | Diferença entre total de créditos e total de débitos em um período |
| **Consolidado Diário** | Resumo agregado de todos os lançamentos de um dia |
| **Microsserviço** | Serviço independente com responsabilidade única e deploy próprio |
| **Event-Driven** | Arquitetura onde serviços comunicam via eventos assíncronos |
| **CQRS** | Padrão de separação entre operações de escrita (Command) e leitura (Query) |
| **Cache-Aside** | Padrão onde a aplicação gerencia manualmente o cache (read-through manual) |
| **Consistência Eventual** | Modelo onde o sistema converge para um estado consistente após um delay aceitável |
| **Dead Letter Queue** | Fila para mensagens que falharam após todas as tentativas de retry |
| **Bounded Context** | Limite explícito dentro do qual um modelo de domínio é definido e aplicável |
| **Idempotência** | Propriedade de uma operação que pode ser executada múltiplas vezes com o mesmo resultado |
| **Outbox Pattern** | Padrão que garante atomicidade entre persistência em banco e publicação de evento |
