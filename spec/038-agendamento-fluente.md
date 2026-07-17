# Spec 038: Agendamento Fluente — Builder, Calendários e GuaraDatas

**Status:** Approved (2026-07-16)
**Date:** 2026-07-16
**Escopo:** feature — estende [Spec 005 (`Guara.Scheduler`)](005-guara-scheduler.md) e [Spec 019 (`Guara.Extensions`)](019-guara-extensions.md); persistência via [Spec 004](004-guara-storage.md)
**Licença:** OSS (core)
**Docs de referência:** [ADR-0009](../docs/adr/0009-politica-de-dependencias.md) · [ADR-0010](../docs/adr/0010-api-do-usuario-em-portugues.md)

## Problem

Agendamentos reais têm mais dimensões que "id + cron": início/fim de vigência, descrição, fila, fuso horário, datas excluídas (feriados). A assinatura posicional `AdicionarOuAtualizarRecorrenteAsync(id, expr, cron, tz, ct)` não escala para isso. O Quartz resolve com um **builder fluente** (`WithIdentity`, `StartAt`, `WithDescription`...), `DateBuilder` e **calendars**. O Guará adota o mesmo modelo — com a API em português (ADR-0010) e **sem terceiros** (ADR-0009: conversão de fuso nativa, sem o pacote TimeZoneConverter).

## Scope

### In

- **Builder fluente de recorrentes** — a forma primária de `AdicionarOuAtualizarRecorrenteAsync`:

```csharp
await jobs.AdicionarOuAtualizarRecorrenteAsync(job => job
    .ComId("limpeza-noturna")
    .Executa(() => LimparRegistrosExpiradosAsync())
    .ComCron("0 3 * * *")
    .NoFusoHorario("America/Sao_Paulo")      // aceita IANA ou Windows
    .IniciaEm(GuaraDatas.SegundoExato(DateTimeOffset.UtcNow.AddSeconds(7)))
    .TerminaEm(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero))
    .ComDescricao("Remove registros expirados da base")
    .NaFila("manutencao")
    .ComCalendario("feriados")               // pula datas excluídas
    .PularSeAnteriorEmExecucao(),            // equivalente fluente do atributo (spec 036)
    ct);
```

- **Agenda por intervalo** (equivalente ao `WithDailyTimeIntervalSchedule` do Quartz), alternativa ao cron: `.ACada(TimeSpan.FromSeconds(10))` (+ janela diária opcional `.EntreHorarios(inicio, fim)`).
- **`GuaraDatas`** — o "DateBuilder" do Guará: helpers estáticos para datas de disparo.
- **Calendários** (equivalente aos calendars do Quartz): datas/dias excluídos, persistidos e reutilizáveis por vários recorrentes; alterar o calendário **recalcula automaticamente** o próximo disparo dos recorrentes que o usam (equivalente do `updateTriggers: true`).
- **Fuso horário Windows/Linux nativo**: `NoFusoHorario` aceita id **IANA** (`America/Sao_Paulo`) ou **Windows** (`E. South America Standard Time`) — normalização via APIs nativas do .NET (`TimeZoneInfo.TryConvertIanaIdToWindowsId`/inverso). O equivalente do `q.UseTimeZoneConverter()` do Quartz é **automático**, sem opt-in e sem pacote de terceiros.
- Sobrecarga simples mantida por conveniência: `AdicionarOuAtualizarRecorrenteAsync(id, expr, cron, tz, ct)`.

### Out

- Cálculo do cron em si — [Spec 005](005-guara-scheduler.md) (parser próprio).
- Atributos declarativos — [Spec 036](036-atributos-de-job.md) (o builder oferece os equivalentes fluentes; **builder vence o atributo** quando ambos definem a mesma coisa).
- Grafos/DAG de agendamento — fora do 1.0.

## Domain Model

- **`IRecurringJobBuilder`** (tipo em inglês, métodos em português — ADR-0010): `ComId` (obrigatório), `Executa` (obrigatório), `ComCron` **ou** `ACada` (exatamente um), `NoFusoHorario`, `IniciaEm`, `TerminaEm`, `ComDescricao`, `NaFila`, `ComCalendario`, `PularSeAnteriorEmExecucao`.
- **`GuaraDatas`** (estático): `SegundoExato(dto)` (trunca ms — como `DateBuilder.EvenSecondDate`), `MinutoExato(dto)`, `HoraExata(dto)`, `HojeAs(hora, min[, tz])`, `AmanhaAs(hora, min[, tz])`, `ProximoDiaUtil([tz])`.
- **`ICalendarBuilder`**: `ExcluirData(DateOnly)`, `ExcluirIntervalo(DateOnly, DateOnly)`, `ExcluirDiasDaSemana(params DayOfWeek[])`, `ExcluirCron(expr)` (janelas por cron).
- **`IGuaraClient`** ganha (extend-only): `AdicionarOuAtualizarRecorrenteAsync(Action<IRecurringJobBuilder>, ct)` e `AdicionarOuAtualizarCalendarioAsync(nome, Action<ICalendarBuilder>, ct)` / `ExcluirCalendarioAsync(nome, ct)`.
- **Persistência**: definição recorrente completa (com vigência/descrição/calendário) na estrutura `Recurring`; calendários na estrutura **`Calendars`** (adicionada ao esquema da [Spec 004](004-guara-storage.md)) via `IRecurringStorage`.
- **Semântica do calendário**: ao computar a próxima ocorrência (Spec 005), datas excluídas são **puladas** para a ocorrência seguinte válida.

## API Contract

```csharp
// Calendários — equivalente ao AddCalendar<HolidayCalendar> do Quartz
await jobs.AdicionarOuAtualizarCalendarioAsync("feriados", cal => cal
    .ExcluirData(new DateOnly(2026, 12, 25))
    .ExcluirData(new DateOnly(2027, 1, 1))
    .ExcluirDiasDaSemana(DayOfWeek.Sunday),
    ct); // alterar o calendário recalcula o NextRun de quem o usa (updateTriggers automático)
```

Tradução Quartz → Guará (referência):

| Quartz | Guará |
|---|---|
| `WithIdentity("x")` | `ComId("x")` |
| `StartAt(...)` / `EndAt(...)` | `IniciaEm(...)` / `TerminaEm(...)` |
| `WithCronSchedule("...")` | `ComCron("...")` |
| `WithDailyTimeIntervalSchedule(x => x.WithInterval(10, Second))` | `ACada(TimeSpan.FromSeconds(10))` [+ `EntreHorarios(...)`] |
| `WithDescription("...")` | `ComDescricao("...")` |
| `DateBuilder.EvenSecondDate(...)` | `GuaraDatas.SegundoExato(...)` |
| `q.UseTimeZoneConverter()` | automático (APIs nativas do .NET — sem terceiros) |
| `q.AddCalendar<HolidayCalendar>(name, replace, updateTriggers, ...)` | `AdicionarOuAtualizarCalendarioAsync(nome, cal => ...)` (replace/updateTriggers implícitos) |

## Authorization

Mesma política do `IGuaraClient` (Specs 005/021).

## Edge Cases & Failure Modes

- **Builder incompleto** (`ComId`/`Executa` ausentes, ou nem `ComCron` nem `ACada`) → exceção clara na chamada — nunca agendamento silencioso inválido.
- **`ComCron` E `ACada` juntos** → erro (exatamente uma agenda).
- **`IniciaEm` no passado** → primeira ocorrência é a próxima válida ≥ agora (documentado).
- **`TerminaEm` atingido** → recorrente fica inativo (visível no dashboard como expirado), não excluído.
- **Calendário inexistente** em `ComCalendario` → erro na chamada (fail-fast).
- **Calendário que exclui todas as ocorrências futuras** (janela vazia) → recorrente marcado sem próxima ocorrência + aviso no log/dashboard.
- **Fuso inválido** (nem IANA nem Windows) → erro claro com sugestão.
- **Exclusão de calendário em uso** → bloqueada (erro listando os recorrentes que o usam) — sem órfãos.

## Non-Functional Requirements

- Builder aloca apenas na configuração (caminho frio); avaliação de calendário O(log n)/O(1) por checagem no cálculo do NextRun.
- Zero terceiros (ADR-0009): fuso via `TimeZoneInfo` nativo; cron próprio (Spec 005).
- Expressões `Executa(() => ...)` compiladas pelo source generator (Spec 019/029) — zero reflection.
- Extend-only: novos métodos do builder não quebram os existentes.

## Integrations

`Guara.Scheduler` (Spec 005) computa ocorrências respeitando vigência e calendários; `IRecurringStorage` persiste `Recurring` e `Calendars` (Spec 004); dashboard (Specs 022/032) exibe descrição, vigência, calendário e ocorrências puladas.

## Acceptance Criteria

- **AC-1 — Builder completo.** *Dado* o exemplo do escopo, *então* o recorrente é criado com id, cron, fuso, início, fim, descrição, fila e calendário persistidos.
- **AC-2 — Upsert.** *Dado* o mesmo `ComId` chamado de novo com outra agenda, *então* a definição é atualizada (não duplicada).
- **AC-3 — Intervalo.** *Dado* `.ACada(10s)` com `.IniciaEm(T)`, *então* dispara em T, T+10s, T+20s...
- **AC-4 — Vigência.** *Dado* `TerminaEm(T)`, *então* nenhuma ocorrência dispara após T e o recorrente aparece como expirado.
- **AC-5 — Calendário exclui.** *Dado* `ComCalendario("feriados")` com 25/12 excluído e cron diário, *então* 25/12 é pulado para 26/12.
- **AC-6 — updateTriggers.** *Dado* um calendário alterado, *então* o `NextRun` dos recorrentes que o usam é recalculado automaticamente.
- **AC-7 — Fuso IANA e Windows.** *Dado* `NoFusoHorario("America/Sao_Paulo")` no Windows e `"E. South America Standard Time"` no Linux, *então* ambos resolvem para o mesmo fuso — sem pacote de terceiros.
- **AC-8 — GuaraDatas.** *Dado* `GuaraDatas.SegundoExato(agora+7s)`, *então* o resultado não tem componente de milissegundos.
- **AC-9 — Validação.** *Dado* builder sem `ComId` ou com `ComCron`+`ACada`, *então* a chamada falha com mensagem clara.
- **AC-10 — Builder vence atributo.** *Dado* `[GuaraFila("a")]` no job e `.NaFila("b")` no builder, *então* vale `"b"`.

## Deferred Decisions

- **DD-1 — Janela diária do intervalo.** *Fallback:* `EntreHorarios(TimeOnly, TimeOnly)` opcional para `ACada`; sem ela, o intervalo corre 24h. *Revisão:* implementação.
- **DD-2 — Tipos de calendário adicionais** (mensal, lunar etc.). *Fallback:* datas/intervalos/dias-da-semana/cron no 1.0; demais extend-only depois. *Revisão:* pós-1.0.
- **DD-3 — `GuaraDatas` com fuso.** *Fallback:* sobrecargas `[, tz]` usam UTC por default. *Revisão:* implementação.

## Open Questions

_(vazio)_
