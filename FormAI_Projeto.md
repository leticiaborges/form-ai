# FormAI
### Gerador de Formulários com Inteligência Artificial
> Documento de Visão e Arquitetura do Projeto

---

## 1. Visão Geral do Projeto

FormAI é uma plataforma educacional que usa Inteligência Artificial para gerar formulários de perguntas automaticamente a partir de conteúdo fornecido pelo usuário — PDFs, documentos Word, texto colado, imagens ou URLs de páginas web.

O professor ou criador de conteúdo faz upload do material, define o contexto e a IA gera um formulário completo com perguntas de seleção única, múltipla escolha, texto livre ou resposta numérica. O formulário gerado pode ser editado livremente antes de ser publicado e compartilhado por link.

**Diferencial em relação a concorrentes (Jotform, Typeform, forms.app):**

- Foco exclusivo em contexto educacional (provas, quizzes, avaliações)
- Controle explícito sobre tipo de pergunta por item gerado
- Análise automática das respostas recebidas via IA
- Arquitetura self-hosted: dados sensíveis não saem do servidor

---

## 2. Stack Tecnológica

### Backend
- .NET 8 + ASP.NET Core — API principal
- Entity Framework Core + PostgreSQL — persistência
- SignalR — notificações em tempo real
- xUnit — testes unitários e de integração
- Claude API (Anthropic) — geração de formulários via IA
- IFormFile / processamento de PDF e imagem — extração de conteúdo

### Frontend
- React + TypeScript
- Tailwind CSS
- Editor drag-and-drop de perguntas (react-beautiful-dnd ou dnd-kit)

### Infraestrutura
- Docker + docker-compose
- GitHub Actions — CI/CD
- Deploy: Railway ou Render

---

## 3. Modelagem de Dados

Seis entidades principais, organizadas em torno do conceito de `Form` (formulário gerado por IA) em vez de `Poll` (enquete simples).

### User

| Campo | Tipo | Descrição |
|---|---|---|
| id | uuid | Identificador único |
| name | string | Nome do usuário |
| email | string | E-mail (único) |
| passwordHash | string | Senha criptografada |
| createdAt | datetime | Data de cadastro |

### Form

| Campo | Tipo | Descrição |
|---|---|---|
| id | uuid | Identificador único |
| title | string | Título do formulário |
| description | string? | Descrição opcional |
| createdBy | uuid | FK → User |
| sourceType | enum | `pdf` \| `word` \| `text` \| `image` \| `url` |
| sourceContent | text? | Conteúdo extraído da fonte |
| sourceUrl | string? | URL (quando sourceType = url) |
| aiPromptContext | text? | Contexto adicional fornecido pelo usuário para a IA |
| isPublic | bool | Formulário acessível por link sem login |
| expiresAt | datetime? | Prazo de submissão (opcional) |
| allowMultipleSubmissions | bool | Permite múltiplos envios |
| showResultsAfterSubmit | bool | Exibe gabarito/resultados após envio |
| createdAt | datetime | Data de criação |

### FormQuestion

| Campo | Tipo | Descrição |
|---|---|---|
| id | uuid | Identificador único |
| formId | uuid | FK → Form |
| text | string | Enunciado da pergunta |
| type | enum | `single` \| `multiple` \| `text` \| `numeric` |
| order | int | Posição no formulário |
| isRequired | bool | Resposta obrigatória |
| aiGenerated | bool | Indica se foi gerada pela IA |
| points | decimal? | Pontuação (para avaliações) |
| correctAnswer | string? | Gabarito (opcional) |

### QuestionOption

Usada para perguntas do tipo `single` e `multiple`.

| Campo | Tipo | Descrição |
|---|---|---|
| id | uuid | Identificador único |
| questionId | uuid | FK → FormQuestion |
| text | string | Texto da opção |
| order | int | Posição entre as opções |
| isCorrect | bool? | Indica alternativa correta |

### Submission

Representa um envio completo do formulário por um respondente.

| Campo | Tipo | Descrição |
|---|---|---|
| id | uuid | Identificador único |
| formId | uuid | FK → Form |
| userId | uuid? | FK → User (nulo se anônimo) |
| respondentToken | uuid | UUID gerado no frontend (localStorage), identifica anônimos |
| ipAddress | string | IP do respondente |
| submittedAt | datetime | Data/hora do envio |
| score | decimal? | Pontuação calculada (se aplicável) |

### Answer

Cada linha representa a resposta a uma pergunta dentro de uma `Submission`.

| Campo | Tipo | Descrição |
|---|---|---|
| id | uuid | Identificador único |
| submissionId | uuid | FK → Submission |
| questionId | uuid | FK → FormQuestion |
| selectedOptionIds | uuid[]? | Opções selecionadas (single/multiple) |
| textValue | string? | Resposta em texto livre |
| numericValue | decimal? | Resposta numérica |

---

## 4. Funcionalidades e Endpoints

### Autenticação

| Método | Endpoint | Descrição |
|---|---|---|
| `POST` | /api/auth/register | Cadastro de novo usuário |
| `POST` | /api/auth/login | Login — retorna JWT |
| `POST` | /api/auth/refresh | Renova o token JWT |

### Geração de Formulário com IA

| Método | Endpoint | Descrição |
|---|---|---|
| `POST` | /api/forms/generate/text | Gera formulário a partir de texto colado |
| `POST` | /api/forms/generate/file | Gera formulário a partir de PDF ou Word (multipart) |
| `POST` | /api/forms/generate/url | Gera formulário a partir de URL (scraping + IA) |
| `POST` | /api/forms/generate/image | Gera formulário a partir de imagem (OCR + IA) |

**Payload de geração (comum a todas as rotas):**

Além do conteúdo fonte, o body aceita:
- `questionCount` — número de perguntas desejadas (default: 10)
- `allowedTypes` — tipos de pergunta permitidos (ex: `["single", "multiple"]`)
- `difficultyLevel` — `easy` | `medium` | `hard` (afeta elaboração das perguntas)
- `context` — instrução adicional para a IA (ex: "foco em datas históricas")
- `includeCorrectAnswers` — bool, se a IA deve marcar o gabarito

### CRUD de Formulários

| Método | Endpoint | Descrição |
|---|---|---|
| `POST` | /api/forms | Salva formulário (gerado ou manual) |
| `GET` | /api/forms | Lista formulários do usuário autenticado |
| `GET` | /api/forms/{id} | Detalhes + perguntas (público ou autenticado) |
| `PUT` | /api/forms/{id} | Edita metadados do formulário (dono) |
| `DELETE` | /api/forms/{id} | Remove formulário (dono) |
| `PATCH` | /api/forms/{id}/close | Encerra recebimento de respostas |
| `PUT` | /api/forms/{id}/questions | Atualiza perguntas (reordenar, editar, remover) |

### Submissões e Respostas

| Método | Endpoint | Descrição |
|---|---|---|
| `POST` | /api/forms/{id}/submit | Envia respostas (anônimo ou autenticado) |
| `GET` | /api/forms/{id}/results | Resultados agregados por pergunta (dono) |
| `GET` | /api/forms/{id}/submissions | Lista submissões individuais (dono) |
| `GET` | /api/forms/{id}/my-submission | Verifica se o respondente já enviou |
| `GET` | /api/forms/{id}/submissions/{sid} | Detalhe de uma submissão (dono) |

### Análise de Resultados com IA

| Método | Endpoint | Descrição |
|---|---|---|
| `POST` | /api/forms/{id}/analyze | IA sumariza padrões de resposta e gera insights |

O endpoint `/analyze` dispara uma chamada à Claude API com as respostas agregadas e retorna um resumo: pontos de dificuldade da turma, alternativas mais erradas, padrões em respostas abertas. O resultado é salvo em cache e exibido no dashboard.

### Tempo Real (SignalR)

- **Hub:** `/hubs/form`
- **Evento `ReceiveSubmission`** — broadcast para o dono quando nova resposta chega
- **Evento `GenerationProgress`** — streaming do progresso da geração de perguntas pela IA

---

## 5. Regras de Negócio

- Formulário expirado não aceita novas submissões
- Sem submissão dupla: validado por `userId` (autenticado) ou `respondentToken` + IP (anônimo)
- Somente o criador pode editar, encerrar ou visualizar resultados individuais
- Formulários públicos: qualquer pessoa com o link pode responder (sem login)
- Formulários privados: exigem autenticação para ver e responder
- Gabarito e pontuação ficam ocultos até o encerramento (configurável)
- Perguntas geradas pela IA podem ser editadas, reordenadas ou removidas antes da publicação
- O conteúdo enviado para geração não é armazenado permanentemente — apenas o texto extraído fica em `sourceContent`

---

## 6. Arquitetura do Backend

Clean Architecture leve em camadas. A integração com a IA é isolada na camada de Infrastructure, exposta via interface na Application.

### Estrutura de diretórios

```
src/
  FormAI.API/              → controllers, middlewares, SignalR hub
  FormAI.Application/      → use cases, DTOs, interfaces, validações
    ├─ Forms/              → CreateForm, GenerateForm, SubmitForm...
    ├─ AI/                 → IFormGenerationService, IAnalysisService
  FormAI.Domain/           → entidades, enums, regras puras
  FormAI.Infrastructure/   → EF Core, repositórios
    ├─ AI/                 → ClaudeFormGenerationService
    ├─ ContentExtraction/  → PdfExtractor, ImageOcrExtractor, UrlScraper
tests/
  FormAI.UnitTests/        → use cases e regras de negócio
  FormAI.IntegrationTests/ → endpoints com WebApplicationFactory
```

### Fluxo de Geração com IA

1. Controller recebe a fonte (arquivo, texto, URL ou imagem)
2. `ContentExtractor` extrai o texto puro do conteúdo
3. `ClaudeFormGenerationService` monta o prompt com contexto + texto extraído + parâmetros (`questionCount`, `types`, `difficulty`)
4. Claude API retorna JSON estruturado com perguntas e opções
5. Application layer valida e mapeia para entidades do domínio
6. Formulário é salvo no banco como rascunho aguardando revisão do usuário
7. SignalR notifica o frontend sobre o progresso (streaming)

---

## 7. Testes

### Unitários
- Regra de submissão duplicada (`userId` e `respondentToken`)
- Formulário expirado rejeita nova submissão
- Somente o dono pode editar ou encerrar
- Mapeamento correto de tipos de pergunta gerados pela IA
- Cálculo de pontuação com gabarito

### Integração
- Fluxo completo: upload de PDF → geração → publicação → submissão → resultados
- Endpoints de autenticação (register, login, refresh)
- Controle de acesso (formulário privado vs. público)
- Testcontainers para PostgreSQL real nos testes de integração
- Mock do `ClaudeFormGenerationService` nos testes de integração

Cobertura mínima esperada: fluxo de geração, submissão e análise de resultados.

---

## 8. Docker

- `Dockerfile` — imagem multi-stage da API (.NET 8)
- `docker-compose.yml` — API + PostgreSQL + volumes para rodar localmente
- Variável `ANTHROPIC_API_KEY` injetada via `.env` (não commitada no repositório)

---

## 9. GitHub Actions — Pipeline CI/CD

Três jobs separados para clareza no histórico do repositório:

| Job | O que faz |
|---|---|
| `build-and-test` | Build do .NET, executa testes unitários e de integração, gera relatório de cobertura |
| `docker-build` | Builda a imagem Docker e faz push para o registry (só em merge na main) |
| `deploy` | Deploy automático na Railway via webhook (só em merge na main) |

---

## 10. Frontend React

Seis telas principais, cobrindo o fluxo completo de criação e resposta:

| Rota | Tela |
|---|---|
| `/` | Home — formulários públicos recentes + CTA para criar |
| `/login` \| `/register` | Autenticação |
| `/forms/new` | Criação: upload/texto/URL/imagem → parâmetros → geração → editor de perguntas |
| `/forms/{id}/edit` | Editor drag-and-drop das perguntas geradas pela IA |
| `/forms/{id}` | Página de resposta pública com progresso em tempo real |
| `/dashboard` | Lista de formulários do usuário, resultados, análise de IA |

---

## 11. Ordem de Implementação

### Fase 1 — Base funcional - parte 1
- Configurar projeto, EF Core, PostgreSQL, migrations
- Autenticação JWT (register, login, refresh)
- CRUD de formulários e perguntas

### Fase 1.1 — Base funcional - parte 2
- Integração básica com Claude API (geração por texto)
- Endpoint de submissão com validação de duplicidade

### Fase 2 — Extração de conteúdo
- `PdfExtractor` (iTextSharp ou PdfPig)
- `ImageOcrExtractor` (Tesseract ou API de visão do Claude)
- `UrlScraper` (HtmlAgilityPack + limpeza de conteúdo)
- Endpoints `/generate/file`, `/generate/image` e `/generate/url`
- Parâmetros de geração (`questionCount`, `types`, `difficulty`, `context`)

### Fase 3 — Qualidade e infraestrutura
- Testes unitários (regras de negócio)
- Testes de integração (endpoints com Testcontainers)
- Dockerfile + docker-compose funcionando localmente
- GitHub Actions: build + test + docker

### Fase 4 — Tempo real, análise e deploy
- SignalR: progresso de geração e novas submissões
- Endpoint `/analyze` — IA sumariza resultados
- Deploy na Railway + pipeline completo de CD

### Fase 5 — Frontend React
- Telas de autenticação
- Tela de criação com upload e parâmetros de geração
- Editor drag-and-drop de perguntas
- Tela de resposta pública
- Dashboard com resultados e análise de IA

---

## 12. Considerações de Segurança e Privacidade

- A `ANTHROPIC_API_KEY` nunca é commitada — sempre via variável de ambiente
- Conteúdo enviado para geração é descartado após extração de texto (não armazenado em arquivo)
- Formulários privados retornam `403` para não-autenticados
- Rate limiting nos endpoints de geração (custo de API por chamada)
- Sanitização do conteúdo extraído antes de enviar ao modelo
