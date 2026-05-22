# TelegramBotMucini

Bot Telegram MVC/WebApi per inserimento rapportini.

## Configurazione locale

Il file `btTelegramWebBot/Web.config` contiene token Telegram, chiavi API e stringhe di connessione, quindi non viene versionato.

Per una nuova installazione occorre creare `btTelegramWebBot/Web.config` a partire dalla configurazione dell'ambiente di destinazione e valorizzare almeno:

- `TelegramToken`
- `ReminderApiKey`
- `connBT`
- `conn2BT`
- `connBTS`
- `conn2BTS`
- `BotMessageDeleteAfterMinutes`
- `TimesheetReminderSlots`
- `TimesheetReminderRetryUntilMinutes`

Gli script SQL da applicare al database sono nella cartella `Database`, in ordine di nome.
