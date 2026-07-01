Imports System
Imports System.Linq
Imports Telegram.Bot.Types
Imports System.Net
Imports System.Net.Mail
Imports System.IO
Imports System.Configuration
Imports TelegramAuthLibrary
Imports System.Xml
Imports System.Xml.Serialization
Imports Newtonsoft.Json
Imports System.Collections.Generic
Imports Telegram.Bot
Imports AegisImplicitMail
Imports System.Web.Http
Imports System.Threading
Imports System.Data
Imports System.Data.OleDb
Imports System.Data.SqlClient
Imports System.Text
Imports System.Globalization

Namespace btTelegramWebBot.Controllers
    Public Class UpdateController
        Inherits ApiController

        Private botClient As ITelegramBotClient

        Public Sub New()

            Dim token = ConfigurationManager.AppSettings("TelegramToken").ToString()
            Try
                botClient = New TelegramBotClient(token)
            Catch ex As Exception
                botClient = Nothing
            End Try
        End Sub

        ' POST api/update
        <HttpPost>
        Public Function Hook(ByVal update As Update) As IHttpActionResult
            Dim body = ControllerContext.GetRawPostData()
            Dim pathLog = SetLogPath()

            Try
                Dim operazione = ""

                Select Case update.Type
                    Case Enums.UpdateType.Unknown
                    Case Enums.UpdateType.Message
                        GestisciMessaggio(update)
                        operazione = "ricevuto messaggio con Id " & update.Message.MessageId.ToString() & " dall'utente " & update.Message.Chat.Id.ToString()
                    Case Enums.UpdateType.InlineQuery
                    Case Enums.UpdateType.ChosenInlineResult
                    Case Enums.UpdateType.CallbackQuery
                        GestisciCallbackQuery(update)
                        operazione = "ricevuta callback query con Id " & update.CallbackQuery.Id & " dall'utente " & update.CallbackQuery.Message.Chat.Id.ToString()
                    Case Enums.UpdateType.EditedMessage
                    Case Enums.UpdateType.ChannelPost
                    Case Enums.UpdateType.EditedChannelPost
                    Case Enums.UpdateType.ShippingQuery
                    Case Enums.UpdateType.PreCheckoutQuery
                    Case Enums.UpdateType.Poll
                    Case Enums.UpdateType.PollAnswer
                    Case Else
                End Select

                Using file As StreamWriter = New StreamWriter(pathLog, True)
                    file.WriteLine(Date.Now.ToString() & ": " & operazione)
                End Using

            Catch ex As Exception

                Using file As StreamWriter = New StreamWriter(pathLog, True)
                    file.WriteLine(Date.Now.ToString() & ": ERRORE " & ex.Message)
                    file.WriteLine(New String(" "c, 21) & body)
                End Using
            End Try

            Return MyBase.Ok()
        End Function

        ' GET api/reminders/worklocation
        ' Endpoint pensato per essere richiamato da Windows Service o Operazioni Pianificate alle 08:00 e alle 13:00.
        <HttpGet>
        <Route("api/reminders/worklocation")>
        Public Function WorkLocationReminder(Optional ByVal key As String = "") As IHttpActionResult
            Dim configuredKey = ConfigurationManager.AppSettings("ReminderApiKey")
            If Not String.IsNullOrEmpty(configuredKey) AndAlso Not String.Equals(configuredKey, key) Then
                Return MyBase.Unauthorized()
            End If

            Dim sent = SendWorkLocationReminderToAll(Date.Now)
            Return MyBase.Ok(New With {.sent = sent})
        End Function

        ' GET api/reminders/timesheets
        ' Endpoint pensato per essere richiamato ogni 30 minuti dalle 17:00 alle 19:00.
        <HttpGet>
        <Route("api/reminders/timesheets")>
        Public Function TimesheetReminder(Optional ByVal key As String = "", Optional ByVal chatId As String = "") As IHttpActionResult
            Dim configuredKey = ConfigurationManager.AppSettings("ReminderApiKey")
            If Not String.IsNullOrEmpty(configuredKey) AndAlso Not String.Equals(configuredKey, key) Then
                Return MyBase.Unauthorized()
            End If

            Try
                Dim sent = SendTimesheetReminderToAll(Date.Now, chatId)
                Return MyBase.Ok(New With {.sent = sent})
            Catch ex As Exception
                ScriviLogBot("ERRORE TimesheetReminder: " & ex.ToString())
                Return MyBase.InternalServerError(ex)
            End Try
        End Function

        ' GET api/reminders/cleanupmessages
        ' Cancella da Telegram i messaggi del bot la cui scadenza e' passata.
        <HttpGet>
        <Route("api/reminders/cleanupmessages")>
        Public Function CleanupBotMessages(Optional ByVal key As String = "") As IHttpActionResult
            Dim configuredKey = ConfigurationManager.AppSettings("ReminderApiKey")
            If Not String.IsNullOrEmpty(configuredKey) AndAlso Not String.Equals(configuredKey, key) Then
                Return MyBase.Unauthorized()
            End If

            Try
                Dim deleted = DeleteExpiredBotMessages()
                Return MyBase.Ok(New With {.deleted = deleted})
            Catch ex As Exception
                ScriviLogBot("ERRORE CleanupBotMessages: " & ex.ToString())
                Return MyBase.InternalServerError(ex)
            End Try
        End Function

        ' GET api/reminders/recentclientscache
        ' Aggiorna la cache dei clienti recenti usata dal flusso rapportini.
        <HttpGet>
        <Route("api/reminders/recentclientscache")>
        Public Function RefreshRecentClientsCache(Optional ByVal key As String = "", Optional ByVal days As Integer = 15, Optional ByVal full As Boolean = False) As IHttpActionResult
            Dim configuredKey = ConfigurationManager.AppSettings("ReminderApiKey")
            If Not String.IsNullOrEmpty(configuredKey) AndAlso Not String.Equals(configuredKey, key) Then
                Return MyBase.Unauthorized()
            End If

            Try
                If days < 1 Then days = 15

                Dim updated = 0
                updated += RefreshRecentClientsCacheForCompany("BestTool", ConfigurationManager.AppSettings("connBT").ToString(), days, full)
                updated += RefreshRecentClientsCacheForCompany("BestToolService", ConfigurationManager.AppSettings("connBTS").ToString(), days, full)
                Return MyBase.Ok(New With {.updated = updated, .days = days, .full = full})
            Catch ex As Exception
                ScriviLogBot("ERRORE RefreshRecentClientsCache: " & ex.ToString())
                Return MyBase.InternalServerError(ex)
            End Try
        End Function

        Private Function RefreshRecentClientsCacheForCompany(ByVal azienda As String, ByVal sourceConnectionString As String, ByVal days As Integer, ByVal full As Boolean) As Integer
            Dim data As DataTable

            If full Then
                DeleteRecentClientsCacheForCompany(azienda)
                data = LoadRecentClientsForCache(sourceConnectionString, "20210101", 10)
            Else
                Dim cutoffDate = Date.Today.AddDays(-days).ToString("yyyyMMdd")
                data = LoadRecentClientsForCache(sourceConnectionString, cutoffDate, 5)
            End If

            Dim updated = UpsertRecentClientsCacheRows(azienda, data)
            TrimRecentClientsCache(azienda, GetSubjectsFromRecentClientsCacheData(data), 10)
            Return updated
        End Function

        Private Function LoadRecentClientsForCache(ByVal connectionString As String, ByVal fromDate As String, ByVal maxClientsPerSubject As Integer) As DataTable
            Dim table As New DataTable()
            Dim sql = "select RapportiCodiceSoggetto, ClientiCodice, ClientiRagioneSociale, DataUltimoIntervento, UltimoRapportiCodice " &
                      " from (" &
                      "     select X.*, row_number() over (partition by X.RapportiCodiceSoggetto order by X.DataUltimoIntervento desc, X.UltimoRapportiCodice desc) as RowNumber " &
                      "     from (" &
                      "         select R.RapportiCodiceSoggetto, C.ClientiCodice, C.ClientiRagioneSociale, " &
                      "                max(R.RapportiData) as DataUltimoIntervento, max(R.RapportiCodice) as UltimoRapportiCodice " &
                      "         from Rapporti R inner join Clienti C on C.ClientiCodice = R.RapportiCodiceCliente " &
                      "         where R.RapportiStato <> 'A' and R.RapportiData >= ? and R.RapportiCodiceSoggetto is not null " &
                      "           and C.ClientiCodice <> 99999 and C.ClientiStato <> 'A' " &
                      "         group by R.RapportiCodiceSoggetto, C.ClientiCodice, C.ClientiRagioneSociale " &
                      "     ) X " &
                      " ) Ranked where RowNumber <= ?"

            Using cn As New OleDbConnection(connectionString)
                cn.Open()
                Using cmd As New OleDbCommand(sql, cn)
                    cmd.Parameters.AddWithValue("@p1", fromDate)
                    cmd.Parameters.AddWithValue("@p2", maxClientsPerSubject)
                    Using adapter As New OleDbDataAdapter(cmd)
                        adapter.Fill(table)
                    End Using
                End Using
            End Using

            Return table
        End Function

        Private Sub DeleteRecentClientsCacheForCompany(ByVal azienda As String)
            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                Using cmd As New OleDbCommand("delete from TelegramBotRecentClientsCache where Azienda = ?", cn)
                    cmd.Parameters.AddWithValue("@p1", azienda)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        Private Function UpsertRecentClientsCacheRows(ByVal azienda As String, ByVal data As DataTable) As Integer
            If data Is Nothing OrElse data.Rows.Count = 0 Then Return 0

            Dim updated = 0
            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                For Each row As DataRow In data.Rows
                    Dim updateSql = "update TelegramBotRecentClientsCache set ClientiRagioneSociale = ?, DataUltimoIntervento = ?, UltimoRapportiCodice = ?, UpdatedAt = ? " &
                                    " where Azienda = ? and SoggettiCodice = ? and ClientiCodice = ?"
                    Using cmd As New OleDbCommand(updateSql, cn)
                        cmd.Parameters.AddWithValue("@p1", row.Item("ClientiRagioneSociale").ToString())
                        cmd.Parameters.AddWithValue("@p2", Convert.ToDateTime(row.Item("DataUltimoIntervento")))
                        cmd.Parameters.AddWithValue("@p3", Convert.ToInt32(row.Item("UltimoRapportiCodice")))
                        cmd.Parameters.AddWithValue("@p4", Date.Now)
                        cmd.Parameters.AddWithValue("@p5", azienda)
                        cmd.Parameters.AddWithValue("@p6", Convert.ToInt32(row.Item("RapportiCodiceSoggetto")))
                        cmd.Parameters.AddWithValue("@p7", Convert.ToInt32(row.Item("ClientiCodice")))
                        If cmd.ExecuteNonQuery() > 0 Then
                            updated += 1
                            Continue For
                        End If
                    End Using

                    Dim insertSql = "insert into TelegramBotRecentClientsCache (Azienda, SoggettiCodice, ClientiCodice, ClientiRagioneSociale, DataUltimoIntervento, UltimoRapportiCodice, UpdatedAt) " &
                                    " values (?, ?, ?, ?, ?, ?, ?)"
                    Using cmd As New OleDbCommand(insertSql, cn)
                        cmd.Parameters.AddWithValue("@p1", azienda)
                        cmd.Parameters.AddWithValue("@p2", Convert.ToInt32(row.Item("RapportiCodiceSoggetto")))
                        cmd.Parameters.AddWithValue("@p3", Convert.ToInt32(row.Item("ClientiCodice")))
                        cmd.Parameters.AddWithValue("@p4", row.Item("ClientiRagioneSociale").ToString())
                        cmd.Parameters.AddWithValue("@p5", Convert.ToDateTime(row.Item("DataUltimoIntervento")))
                        cmd.Parameters.AddWithValue("@p6", Convert.ToInt32(row.Item("UltimoRapportiCodice")))
                        cmd.Parameters.AddWithValue("@p7", Date.Now)
                        cmd.ExecuteNonQuery()
                        updated += 1
                    End Using
                Next
            End Using

            Return updated
        End Function

        Private Function GetSubjectsFromRecentClientsCacheData(ByVal data As DataTable) As List(Of Integer)
            Dim subjects As New List(Of Integer)
            If data Is Nothing Then Return subjects

            For Each row As DataRow In data.Rows
                Dim soggettiCodice = Convert.ToInt32(row.Item("RapportiCodiceSoggetto"))
                If Not subjects.Contains(soggettiCodice) Then subjects.Add(soggettiCodice)
            Next

            Return subjects
        End Function

        Private Sub TrimRecentClientsCache(ByVal azienda As String, ByVal soggetti As List(Of Integer), ByVal maxClientsPerSubject As Integer)
            If soggetti Is Nothing OrElse soggetti.Count = 0 Then Exit Sub

            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                For Each soggettiCodice In soggetti
                    Dim sql = "delete from TelegramBotRecentClientsCache " &
                              " where Azienda = ? and SoggettiCodice = ? and TelegramBotRecentClientsCacheId not in (" &
                              "     select TelegramBotRecentClientsCacheId from (" &
                              "         select top " & maxClientsPerSubject.ToString() & " TelegramBotRecentClientsCacheId " &
                              "         from TelegramBotRecentClientsCache " &
                              "         where Azienda = ? and SoggettiCodice = ? " &
                              "         order by DataUltimoIntervento desc, UltimoRapportiCodice desc" &
                              "     ) KeepRows" &
                              " )"
                    Using cmd As New OleDbCommand(sql, cn)
                        cmd.Parameters.AddWithValue("@p1", azienda)
                        cmd.Parameters.AddWithValue("@p2", soggettiCodice)
                        cmd.Parameters.AddWithValue("@p3", azienda)
                        cmd.Parameters.AddWithValue("@p4", soggettiCodice)
                        cmd.ExecuteNonQuery()
                    End Using
                Next
            End Using
        End Sub

        Private Function SetLogPath() As String
            Dim path = ConfigurationManager.AppSettings("logPath").ToString()
            Dim pathAnno = IO.Path.Combine(path, Date.Today.Year.ToString())

            If Not Directory.Exists(pathAnno) Then
                Directory.CreateDirectory(pathAnno)
            End If

            Dim pathMese = IO.Path.Combine(pathAnno, Date.Today.Month.ToString())

            If Not Directory.Exists(pathMese) Then
                Directory.CreateDirectory(pathMese)
            End If

            Dim pathLog = IO.Path.Combine(pathMese, Date.Today.Day.ToString() & ".txt")
            Return pathLog
        End Function

        Private Sub ScriviLogBot(ByVal text As String)
            Try
                Using file As StreamWriter = New StreamWriter(SetLogPath(), True)
                    file.WriteLine(Date.Now.ToString() & ": " & text)
                End Using
            Catch ex As Exception
            End Try
        End Sub

        Private Sub GestisciCallbackQuery(ByVal update As Update)

            Dim cn As OleDbConnection
            Dim cn2 As OleDbConnection
            Dim cmd As OleDbCommand
            Dim rs As OleDbDataReader
            cn = New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
            Dim Sql As String = ""
            Dim xxSoggettiCodice As String = ""
            Dim xxAzienda As String = ""
            Dim xxCon As String = ""

            If Not Equals(update.CallbackQuery.Data, String.Empty) Then
                Dim callbackAnswerThread As New Thread(New ParameterizedThreadStart(AddressOf AnswerCallbackQueryAsync))
                callbackAnswerThread.Start(update.CallbackQuery.Id)
                'Console.WriteLine($"Ricevuta Callback Query dall'ID {update.CallbackQuery.ChatInstance}.");
                'var pb = JsonConvert.DeserializeObject<NextCalendarDay>(e.CallbackQuery.Data);
                Dim valori = update.CallbackQuery.Data.Split(New String() {"||"}, StringSplitOptions.None)
                Dim action = valori(0).Trim()
                If action.StartsWith("TS_", StringComparison.OrdinalIgnoreCase) AndAlso
                   Not String.Equals(action, "TS_START", StringComparison.OrdinalIgnoreCase) AndAlso
                   Not String.Equals(action, "TS_MORE", StringComparison.OrdinalIgnoreCase) AndAlso
                   Not String.Equals(action, "TS_CANCEL", StringComparison.OrdinalIgnoreCase) AndAlso
                   valori.Length > 1 AndAlso
                   IsTimesheetDraftClosed(update.CallbackQuery.Message.Chat.Id.ToString(), valori(1).Trim()) Then
                    SendTextMessage(update.CallbackQuery.Message.Chat.Id.ToString(), "Questo inserimento rapportino e gia stato chiuso.", "", "", "")
                    Exit Sub
                End If

                Dim TitoloCalendario As String = ""
                Select Case action


                    Case "SetCalendar2"
                        Select Case valori(2)
                            Case "2" ' Lavoro da casa
                                TitoloCalendario = "Lavoro da casa"
                            Case "3" ' c/o Best Tool (BO)
                                TitoloCalendario = "c/to Best Tool (BO)"
                            Case "4" ' c/o Best Tool (TV)
                                TitoloCalendario = "c/to Best Tool (TV)"
                            Case "5" ' FERIE !!!
                                TitoloCalendario = "FERIE !!!"
                            Case "6" ' Malattia
                                TitoloCalendario = "Malattia"
                            Case "100"
                                TitoloCalendario = "c/to Cliente"
                        End Select


                        Dim xxTelegramBotParametri As String = ""
                        Dim xxTelegramBotMessage As String = ""
                        Dim xxTelegramBotChatId As String = ""
                        Dim xxTelegramBotParametriPut As String = ""
                        Dim xxTelegramBotFunzione As String = ""
                        Dim xxTelegramBotFunzionePut As String = ""
                        Dim xxTelegramBotMessagePut As String = ""
                        'Dim xxFunzione As String = ""
                        'Dim xxParametri As String = ""

                        cn.Open()
                        Sql = "select TelegramBotMessage, TelegramBotParametri, TelegramBotChatId, TelegramBotFunzione from TelegramBot where TelegramBotId = " & valori(1).Trim
                        cmd = New OleDbCommand(Sql, cn)
                        rs = cmd.ExecuteReader
                        If rs.Read Then
                            xxTelegramBotParametri = rs.Item("TelegramBotParametri").ToString
                            xxTelegramBotFunzione = rs.Item("TelegramBotFunzione").ToString
                            xxTelegramBotMessage = TitoloCalendario
                            xxTelegramBotChatId = rs.Item("TelegramBotChatId").ToString
                            xxTelegramBotParametriPut = xxTelegramBotParametri
                            xxTelegramBotFunzionePut = xxTelegramBotFunzione
                            xxTelegramBotMessagePut = rs.Item("TelegramBotMessage").ToString
                        End If
                        cn.Close()
                        Dim xxParametri = xxTelegramBotParametri.Split(New String() {"||"}, StringSplitOptions.None) 'Soggetto, Data1, chatid

                        If valori(2) = "99" Then
                            cn.Open()
                            Sql = "select ClientiSediDescrizione from ClientiSedi where ClientiSediCodice = " & valori(3).Trim
                            cmd = New OleDbCommand(Sql, cn)
                            rs = cmd.ExecuteReader
                            If rs.Read Then
                                TitoloCalendario = rs.Item("ClientiSediDescrizione").ToString
                                xxParametri(2) = xxTelegramBotChatId
                                xxTelegramBotMessage = TitoloCalendario
                                xxTelegramBotParametriPut = xxTelegramBotParametri
                                xxTelegramBotFunzionePut = xxTelegramBotFunzione
                            End If
                            cn.Close()
                        End If

                        If valori(2) <> "1" Then
                            SetCalendar(xxTelegramBotMessage, xxParametri(1), xxParametri(2).Trim, xxParametri(0).Trim, xxTelegramBotMessagePut, xxTelegramBotFunzionePut, xxTelegramBotParametriPut)
                        Else
                            Dim xxSendMessage As Object
                            Dim xxChiave As String = ""
                            Dim xxParametri2 As String = ""
                            xxSendMessage = TelegramBotSQL2(xxParametri(2).Trim, "", Date.Now().ToString("yyyy-MM-dd HH:mm:ss").ToString, "S", xxTelegramBotMessagePut, "SetCalendar2", xxTelegramBotParametri.ToString)
                            xxChiave = xxSendMessage.ToString
                            Dim xxID As Object

                            'CERCO IL SOGGETTO
                            Dim AUT00 As New DataTable("AUT00")

                            'xxTelegramBotChatId = "1730955618"

                            AUT00 = VerificaAutorizzazioni(xxTelegramBotChatId)
                            xxSoggettiCodice = AUT00.Rows(0).Item("SoggettiCodice").ToString
                            xxAzienda = AUT00.Rows(0).Item("Azienda").ToString


                            Select Case xxAzienda
                                Case "BestTool"
                                    cn2 = New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                                    xxCon = ConfigurationManager.AppSettings("conn2BT").ToString()
                                Case "BestToolService"
                                    cn2 = New OleDbConnection(ConfigurationManager.AppSettings("connBTS").ToString())
                                    xxCon = ConfigurationManager.AppSettings("conn2BTS").ToString()
                            End Select

                            'Cerco i 10 clienti
                            Dim myData(0 To 10, 0 To 1) As String
                            Dim contaIndici As Integer = -1
                            Dim Pulsanti As String = ""
                            Sql = "select TOP 9 " &
                                    " a2.ClientiSediCodice, A2.ClientiSediDescrizione, " &
                                    " (select max(rapportidata) from Rapporti as B1 " &
                                    " inner join ClientiSedi as B2 on B2.ClientiSediCodice = B1.RapportiCodiceClienteSede " &
                                    " where B1.RapportiData >= '20210101' and B1.RapportiCodiceSoggetto = " & xxSoggettiCodice & " and A2.ClientiSediCodice = B2.ClientiSediCodice " &
                                    " group by B2.ClientiSediCodice) as DataUltimoIntervento " &
                                    " from  " &
                                    " Rapporti as A1 " &
                                    " inner join ClientiSedi as A2 on a2.ClientiSediCodice = a1.RapportiCodiceClienteSede " &
                                    " where a1.RapportiData >= '20210101' and a1.RapportiCodiceSoggetto = " & xxSoggettiCodice & " and a2.ClientiSediCodiceCliente <> 99999  " &
                                    " group by a2.ClientiSediCodice, A2.ClientiSediDescrizione " &
                                    " order by DataUltimoIntervento desc "


                            'If CInt(My.Settings.DebugLivello) > 0 Then ScriviLogFile(DateTime.Now.ToString("yyyyMMdd HH:mm:ss") & " - " & sql2)
                            Try
                                cn2.Open()
                                cmd = New OleDbCommand(Sql, cn2)
                                rs = cmd.ExecuteReader
                                Do While rs.Read
                                    contaIndici += 1
                                    Pulsanti += "[{""text"":""" & rs.Item("ClientiSediDescrizione").ToString & """,""callback_data"":""" & "SetCalendar2||" & xxChiave.ToString & "||99||" & rs.Item("ClientiSediCodice").ToString & "|||" & """}],"
                                    'myData(contaIndici, 0) = rs.Item("ClientiSediDescrizione").ToString
                                    'myData(contaIndici, 1) = "SetCalendar2||" & xxChiave.ToString & "||" & CInt(contaIndici + 11).ToString & "|||"
                                Loop
                                cn2.Close()
                                contaIndici += 1
                                Pulsanti += "[{""text"":""c/to Cliente Generico"",""callback_data"":""" & "SetCalendar2||" & xxChiave.ToString & "||100||" & """}]"

                                xxID = ProattivoAggiungiAgendaSceltaCliente(xxParametri(2).Trim, "Scelta cliente", Pulsanti, "", "")

                                Sql = "UPDATE TelegramBot Set TelegramBotMessageId = '" & xxID.ToString & "' where TelegramBotID = '" & xxChiave.ToString & "'"
                                Try
                                    cn.Open()
                                    cmd = New OleDbCommand(Sql, cn)
                                    cmd.ExecuteNonQuery()
                                    cn.Close()
                                Catch ex As Exception

                                    Exit Sub
                                End Try

                            Catch ex As Exception

                                Exit Sub
                            End Try

                        End If

                    Case "SetCalendar"
                        Dim xxTelegramBotParametri As String = ""
                        Dim xxTelegramBotMessage As String = ""
                        cn.Open()
                        Sql = "select TelegramBotMessage, TelegramBotParametri from TelegramBot where TelegramBotId = " & valori(1).Trim
                        cmd = New OleDbCommand(Sql, cn)
                        rs = cmd.ExecuteReader
                        If rs.Read Then
                            xxTelegramBotParametri = rs.Item("TelegramBotParametri").ToString
                            xxTelegramBotMessage = rs.Item("TelegramBotMessage").ToString.Substring(2)
                        End If
                        cn.Close()
                        Dim xxParametri = xxTelegramBotParametri.Split(New String() {"||"}, StringSplitOptions.None) 'Soggetto, Data1, data2, chatid
                        Dim QualeData As String = ""
                        If valori(2) = "1" Then
                            QualeData = xxParametri(1)
                        End If
                        If valori(2) = "2" Then
                            QualeData = xxParametri(2)
                        End If
                        If valori(2) = "3" Then
                            QualeData = xxParametri(3)
                        End If
                        If valori(2) = "4" Then
                            QualeData = xxParametri(4)
                        End If
                        SetCalendar(xxTelegramBotMessage, QualeData, xxParametri(5).Trim, xxParametri(0).Trim, xxTelegramBotMessage, "InserisciInAgenda", "")
                    Case "GetCalendar"
                        GetCalendar(valori(1).Trim(), valori(2).Trim(), valori(3).Trim())
                    Case "Registra"
                        Registra(valori(1).Trim(), valori(2).Trim(), valori(3).Trim())
                    Case "WL"
                        RegistraWorkLocationScelta(update.CallbackQuery.Message.Chat.Id.ToString(), valori(1).Trim(), valori(2).Trim())
                    Case "WLA"
                        RichiediWorkLocationAltro(update.CallbackQuery.Message.Chat.Id.ToString(), valori(1).Trim())
                    Case "TS_START"
                        If valori.Length > 1 Then
                            StartTimesheetDraftFromReminder(update.CallbackQuery.Message.Chat.Id.ToString(), valori(1).Trim())
                        Else
                            StartTimesheetDraft(update.CallbackQuery.Message.Chat.Id.ToString())
                        End If
                    Case "TS_DATE"
                        TimesheetSetDate(update.CallbackQuery.Message.Chat.Id.ToString(), valori(1).Trim(), valori(2).Trim())
                    Case "TS_DATE_OTHER"
                        TimesheetAskCustomDate(update.CallbackQuery.Message.Chat.Id.ToString(), valori(1).Trim())
                    Case "TS_NOT_WORKING"
                        TimesheetAskReturnDate(update.CallbackQuery.Message.Chat.Id.ToString(), valori(1).Trim())
                    Case "TS_RETURN_DATE"
                        TimesheetSetReturnDate(update.CallbackQuery.Message.Chat.Id.ToString(), valori(1).Trim(), valori(2).Trim())
                    Case "TS_RETURN_OTHER"
                        TimesheetAskCustomReturnDate(update.CallbackQuery.Message.Chat.Id.ToString(), valori(1).Trim())
                    Case "TS_CLIENT"
                        TimesheetSetClient(update.CallbackQuery.Message.Chat.Id.ToString(), valori(1).Trim(), valori(2).Trim())
                    Case "TS_CLIENT_SEARCH"
                        TimesheetAskClientSearch(update.CallbackQuery.Message.Chat.Id.ToString(), valori(1).Trim())
                    Case "TS_SITE"
                        TimesheetSetSite(update.CallbackQuery.Message.Chat.Id.ToString(), valori(1).Trim(), valori(2).Trim())
                    Case "TS_HOURS"
                        TimesheetSetHours(update.CallbackQuery.Message.Chat.Id.ToString(), valori(1).Trim(), valori(2).Trim())
                    Case "TS_MORE"
                        StartTimesheetDraft(update.CallbackQuery.Message.Chat.Id.ToString())
                    Case "TS_CANCEL"
                        CancelTimesheetDraft(update.CallbackQuery.Message.Chat.Id.ToString(), valori(1).Trim())
                    Case "CLEAR_CHAT_CONFIRM"
                        ConfirmClearChat(update.CallbackQuery.Message.Chat.Id.ToString())
                    Case "CLEAR_CHAT_CANCEL"
                    Case Else
                End Select
            End If
        End Sub

        Private Function SendWorkLocationReminderToAll(ByVal dataOraInvio As DateTime) As Integer
            Dim sent As Integer = 0
            Dim cnBT As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())

            If Not IsWorkDay(cnBT, dataOraInvio.Date) Then Return 0

            sent += SendWorkLocationReminderForCompany("BestTool", ConfigurationManager.AppSettings("connBT").ToString(), dataOraInvio)
            sent += SendWorkLocationReminderForCompany("BestToolService", ConfigurationManager.AppSettings("connBTS").ToString(), dataOraInvio)

            Return sent
        End Function

        Private Function SendWorkLocationReminderForCompany(ByVal azienda As String, ByVal connectionString As String, ByVal dataOraInvio As DateTime) As Integer
            Dim sent As Integer = 0
            Dim slot = If(dataOraInvio.Hour < 12, "08", "13")
            Dim sql = "select '" & azienda.Replace("'", "''") & "' as Azienda, DeviceID, SoggettiCodice, " &
                      " iif(isnull(SoggettiSoprannome, '') = '', SoggettiNome, SoggettiSoprannome) as SoggettiSoprannome " &
                      " from Device inner join Utenti on UtentiID = DeviceUtenteID inner join Soggetti on SoggettiCodice = UtentiCodiceSoggetto " &
                      " where SoggettiStato <> 'A' and UtentiStato <> 'A' and DeviceStato <> 'A' and isnull(DeviceTelegram, '') = 'S' and isnull(DeviceID, '') <> '' "

            Using cn As New OleDbConnection(connectionString)
                cn.Open()
                Using cmd As New OleDbCommand(sql, cn)
                    Using rs = cmd.ExecuteReader()
                        Do While rs.Read()
                            Dim chatId = rs.Item("DeviceID").ToString()
                            Dim soggettiCodice = rs.Item("SoggettiCodice").ToString()
                            Dim soprannome = rs.Item("SoggettiSoprannome").ToString()

                            Dim reminderId = CInt(CreaWorkLocationReminder(chatId, soggettiCodice, azienda, dataOraInvio.Date, slot))
                            If reminderId > 0 Then
                                Dim keyboardJson = BuildWorkLocationKeyboard(connectionString, soggettiCodice, reminderId)
                                Dim text = "Buongiorno" & If(String.IsNullOrEmpty(soprannome), "", " " & soprannome) & ", dove stai lavorando oggi?"
                                If slot = "13" Then text = "Ciao" & If(String.IsNullOrEmpty(soprannome), "", " " & soprannome) & ", confermi dove stai lavorando oggi pomeriggio?"

                                Dim messageId = SendTelegramMessageWithReplyMarkup(chatId, text, keyboardJson)
                                AggiornaWorkLocationReminderMessageId(reminderId, messageId)
                                sent += 1
                            End If
                        Loop
                    End Using
                End Using
            End Using

            Return sent
        End Function

        Private Function IsWorkDay(ByVal cn As OleDbConnection, ByVal giorno As DateTime) As Boolean
            If giorno.DayOfWeek = DayOfWeek.Saturday OrElse giorno.DayOfWeek = DayOfWeek.Sunday Then Return False

            Try
                Using connection As New OleDbConnection(cn.ConnectionString)
                    connection.Open()
                    Using cmd As New OleDbCommand("select CalendarioTipo from Calendario where CalendarioData = ?", connection)
                        cmd.Parameters.AddWithValue("@p1", giorno.ToString("yyyyMMdd"))
                        Dim result = cmd.ExecuteScalar()
                        If result IsNot Nothing AndAlso Not Convert.IsDBNull(result) Then
                            Return String.Equals(result.ToString(), "L", StringComparison.OrdinalIgnoreCase)
                        End If
                    End Using
                End Using
            Catch ex As Exception
                ' Se il calendario gestionale non risponde, resta valido il controllo lun-ven.
            End Try

            Return True
        End Function

        Private Function CreaWorkLocationReminder(ByVal chatId As String, ByVal soggettiCodice As String, ByVal azienda As String, ByVal reminderDate As DateTime, ByVal reminderSlot As String) As Object
            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()

                Using checkCmd As New OleDbCommand("select TelegramBotWorkLocationResponseId from TelegramBotWorkLocationResponses where ChatId = ? and ReminderDate = ? and ReminderSlot = ?", cn)
                    checkCmd.Parameters.AddWithValue("@p1", chatId)
                    checkCmd.Parameters.AddWithValue("@p2", reminderDate)
                    checkCmd.Parameters.AddWithValue("@p3", reminderSlot)
                    Dim existing = checkCmd.ExecuteScalar()
                    If existing IsNot Nothing AndAlso Not Convert.IsDBNull(existing) Then Return 0
                End Using

                Using cmd As New OleDbCommand("insert into TelegramBotWorkLocationResponses (ChatId, SoggettiCodice, Azienda, ReminderDate, ReminderSlot, ResponseType, CreatedAt) values (?, ?, ?, ?, ?, 'PENDING', ?); select @@Identity", cn)
                    cmd.Parameters.AddWithValue("@p1", chatId)
                    cmd.Parameters.AddWithValue("@p2", soggettiCodice)
                    cmd.Parameters.AddWithValue("@p3", azienda)
                    cmd.Parameters.AddWithValue("@p4", reminderDate)
                    cmd.Parameters.AddWithValue("@p5", reminderSlot)
                    cmd.Parameters.AddWithValue("@p6", Date.Now)
                    Return cmd.ExecuteScalar()
                End Using
            End Using
        End Function

        Private Sub AggiornaWorkLocationReminderMessageId(ByVal reminderId As Integer, ByVal messageId As String)
            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                Using cmd As New OleDbCommand("update TelegramBotWorkLocationResponses set TelegramMessageId = ?, SentAt = ? where TelegramBotWorkLocationResponseId = ?", cn)
                    cmd.Parameters.AddWithValue("@p1", messageId)
                    cmd.Parameters.AddWithValue("@p2", Date.Now)
                    cmd.Parameters.AddWithValue("@p3", reminderId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        Private Function BuildWorkLocationKeyboard(ByVal connectionString As String, ByVal soggettiCodice As String, ByVal reminderId As Integer) As String
            Dim keyboard As New List(Of Object)
            Dim sql = "select TOP 5 A2.ClientiSediCodice, A2.ClientiSediDescrizione, max(A1.RapportiData) as DataUltimoIntervento " &
                      " from Rapporti as A1 inner join ClientiSedi as A2 on A2.ClientiSediCodice = A1.RapportiCodiceClienteSede " &
                      " where A1.RapportiData >= '20210101' and A1.RapportiCodiceSoggetto = ? and A2.ClientiSediCodiceCliente <> 99999 " &
                      " group by A2.ClientiSediCodice, A2.ClientiSediDescrizione order by DataUltimoIntervento desc "

            Using cn As New OleDbConnection(connectionString)
                cn.Open()
                Using cmd As New OleDbCommand(sql, cn)
                    cmd.Parameters.AddWithValue("@p1", soggettiCodice)
                    Using rs = cmd.ExecuteReader()
                        Do While rs.Read()
                            Dim row As New List(Of Object)
                            row.Add(New With {.text = rs.Item("ClientiSediDescrizione").ToString(), .callback_data = "WL||" & reminderId.ToString() & "||" & rs.Item("ClientiSediCodice").ToString()})
                            keyboard.Add(row)
                        Loop
                    End Using
                End Using
            End Using

            Dim otherRow As New List(Of Object)
            otherRow.Add(New With {.text = "Altro...", .callback_data = "WLA||" & reminderId.ToString()})
            keyboard.Add(otherRow)

            Return JsonConvert.SerializeObject(New With {.inline_keyboard = keyboard})
        End Function

        Private Function SendTelegramMessageWithReplyMarkup(ByVal chatId As String, ByVal text As String, ByVal replyMarkupJson As String, Optional ByVal parseMode As String = "") As String
            Dim apiToken As String = ConfigurationManager.AppSettings("TelegramToken").ToString()
            Dim urlString = "https://api.telegram.org/bot" & apiToken & "/sendMessage"

            Using client As New WebClient()
                Dim values As New System.Collections.Specialized.NameValueCollection()
                values("chat_id") = chatId
                values("text") = text
                values("reply_markup") = replyMarkupJson
                If Not String.IsNullOrWhiteSpace(parseMode) Then values("parse_mode") = parseMode

                Dim responseBytes = client.UploadValues(urlString, "POST", values)
                Dim readerString = Encoding.UTF8.GetString(responseBytes)
                Dim jsonResulttodict = JsonConvert.DeserializeObject(Of Dictionary(Of String, Object))(readerString)
                Dim messageId = jsonResulttodict.Item("result")("message_id").ToString()
                QueueBotMessageDeletion(chatId, messageId)
                Return messageId
            End Using
        End Function

        Private Function SendTimesheetMessageWithReplyMarkup(ByVal chatId As String, ByVal text As String, ByVal replyMarkupJson As String) As String
            Return SendTelegramMessageWithReplyMarkup(chatId, "<b>" & WebUtility.HtmlEncode(text) & "</b>", replyMarkupJson, "HTML")
        End Function

        Private Sub AnswerCallbackQuery(ByVal callbackQueryId As String)
            Try
                Dim apiToken As String = ConfigurationManager.AppSettings("TelegramToken").ToString()
                Dim urlString = "https://api.telegram.org/bot" & apiToken & "/answerCallbackQuery"

                Using client As New WebClient()
                    Dim values As New System.Collections.Specialized.NameValueCollection()
                    values("callback_query_id") = callbackQueryId
                    client.UploadValues(urlString, "POST", values)
                End Using
            Catch ex As Exception
                ScriviLogBot("ERRORE AnswerCallbackQuery: " & ex.Message)
            End Try
        End Sub

        Private Sub AnswerCallbackQueryAsync(ByVal callbackQueryId As Object)
            AnswerCallbackQuery(callbackQueryId.ToString())
        End Sub

        Private Sub QueueBotMessageDeletion(ByVal chatId As String, ByVal messageId As String)
            Try
                Dim deleteAfterMinutes = GetBotMessageDeleteAfterMinutes()
                If deleteAfterMinutes <= 0 Then Exit Sub

                Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                    cn.Open()
                    Using cmd As New OleDbCommand("insert into TelegramBotMessageDeletes (ChatId, MessageId, DeleteAfter, CreatedAt) values (?, ?, ?, ?)", cn)
                        cmd.Parameters.AddWithValue("@p1", chatId)
                        cmd.Parameters.AddWithValue("@p2", messageId)
                        cmd.Parameters.AddWithValue("@p3", Date.Now.AddMinutes(deleteAfterMinutes))
                        cmd.Parameters.AddWithValue("@p4", Date.Now)
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            Catch ex As Exception
                ScriviLogBot("ERRORE QueueBotMessageDeletion: " & ex.Message)
            End Try
        End Sub

        Private Function GetBotMessageDeleteAfterMinutes() As Integer
            Dim value = ConfigurationManager.AppSettings("BotMessageDeleteAfterMinutes")
            Dim minutes As Integer
            If Integer.TryParse(value, minutes) Then Return minutes
            Return 10
        End Function

        Private Function DeleteExpiredBotMessages() As Integer
            Dim deleted As Integer = 0
            Dim pending As New DataTable()

            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                Using cmd As New OleDbCommand("select top 50 TelegramBotMessageDeleteId, ChatId, MessageId from TelegramBotMessageDeletes where DeletedAt is null and DeleteAfter <= ? and isnull(DeleteFailedCount, 0) < 5 order by DeleteAfter", cn)
                    cmd.Parameters.AddWithValue("@p1", Date.Now)
                    Using adapter As New OleDbDataAdapter(cmd)
                        adapter.Fill(pending)
                    End Using
                End Using
            End Using

            For Each row As DataRow In pending.Rows
                Dim id = Convert.ToInt32(row.Item("TelegramBotMessageDeleteId"))
                Dim chatId = row.Item("ChatId").ToString()
                Dim messageId = row.Item("MessageId").ToString()

                Try
                    If DeleteTelegramMessage(chatId, messageId) Then
                        MarkBotMessageDeleted(id)
                        deleted += 1
                    Else
                        MarkBotMessageDeleteFailed(id, "deleteMessage returned false")
                    End If
                Catch ex As Exception
                    MarkBotMessageDeleteFailed(id, ex.Message)
                End Try
            Next

            Return deleted
        End Function

        Private Function DeleteQueuedBotMessagesForChat(ByVal chatId As String) As Integer
            Dim deleted As Integer = 0
            Dim pending As New DataTable()

            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                Using cmd As New OleDbCommand("select TelegramBotMessageDeleteId, ChatId, MessageId from TelegramBotMessageDeletes where ChatId = ? and DeletedAt is null and isnull(DeleteFailedCount, 0) < 5 order by MessageId desc", cn)
                    cmd.Parameters.AddWithValue("@p1", chatId)
                    Using adapter As New OleDbDataAdapter(cmd)
                        adapter.Fill(pending)
                    End Using
                End Using
            End Using

            For Each row As DataRow In pending.Rows
                Dim id = Convert.ToInt32(row.Item("TelegramBotMessageDeleteId"))
                Dim messageId = row.Item("MessageId").ToString()

                Try
                    If DeleteTelegramMessage(chatId, messageId) Then
                        MarkBotMessageDeleted(id)
                        deleted += 1
                    Else
                        MarkBotMessageDeleteFailed(id, "deleteMessage returned false")
                    End If
                Catch ex As Exception
                    MarkBotMessageDeleteFailed(id, ex.Message)
                End Try
            Next

            Return deleted
        End Function

        Private Sub AskClearChatConfirmation(ByVal chatId As String)
            Dim keyboard = JsonConvert.SerializeObject(New With {
                .inline_keyboard = New Object() {
                    New Object() {
                        New With {.text = "Sì", .callback_data = "CLEAR_CHAT_CONFIRM"},
                        New With {.text = "No", .callback_data = "CLEAR_CHAT_CANCEL"}
                    }
                }
            })

            SendTelegramMessageWithReplyMarkup(chatId, "Vuoi cancellare tutti i messaggi del bot rimasti visibili in questa chat?", keyboard)
        End Sub

        Private Sub ConfirmClearChat(ByVal chatId As String)
            DeleteQueuedBotMessagesForChat(chatId)
        End Sub

        Private Function DeleteTelegramMessage(ByVal chatId As String, ByVal messageId As String) As Boolean
            Dim apiToken As String = ConfigurationManager.AppSettings("TelegramToken").ToString()
            Dim urlString = "https://api.telegram.org/bot" & apiToken & "/deleteMessage"

            Using client As New WebClient()
                Dim values As New System.Collections.Specialized.NameValueCollection()
                values("chat_id") = chatId
                values("message_id") = messageId

                Dim responseBytes = client.UploadValues(urlString, "POST", values)
                Dim readerString = Encoding.UTF8.GetString(responseBytes)
                Dim result = JsonConvert.DeserializeObject(Of Dictionary(Of String, Object))(readerString)
                Return Convert.ToBoolean(result.Item("ok"))
            End Using
        End Function

        Private Sub MarkBotMessageDeleted(ByVal id As Integer)
            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                Using cmd As New OleDbCommand("update TelegramBotMessageDeletes set DeletedAt = ?, LastError = null where TelegramBotMessageDeleteId = ?", cn)
                    cmd.Parameters.AddWithValue("@p1", Date.Now)
                    cmd.Parameters.AddWithValue("@p2", id)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        Private Sub MarkBotMessageDeleteFailed(ByVal id As Integer, ByVal errorMessage As String)
            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                Using cmd As New OleDbCommand("update TelegramBotMessageDeletes set DeleteFailedCount = isnull(DeleteFailedCount, 0) + 1, LastError = ?, LastAttemptAt = ? where TelegramBotMessageDeleteId = ?", cn)
                    cmd.Parameters.AddWithValue("@p1", If(errorMessage, "").Substring(0, Math.Min(1000, If(errorMessage, "").Length)))
                    cmd.Parameters.AddWithValue("@p2", Date.Now)
                    cmd.Parameters.AddWithValue("@p3", id)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        Private Sub RegistraWorkLocationScelta(ByVal chatId As String, ByVal reminderId As String, ByVal clientiSediCodice As String)
            Dim descrizione = String.Empty
            Dim azienda = String.Empty

            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                Using aziendaCmd As New OleDbCommand("select Azienda from TelegramBotWorkLocationResponses where TelegramBotWorkLocationResponseId = ? and ChatId = ?", cn)
                    aziendaCmd.Parameters.AddWithValue("@p1", reminderId)
                    aziendaCmd.Parameters.AddWithValue("@p2", chatId)
                    Dim result = aziendaCmd.ExecuteScalar()
                    If result IsNot Nothing AndAlso Not Convert.IsDBNull(result) Then azienda = result.ToString()
                End Using
            End Using

            Dim lookupConnectionString = If(String.Equals(azienda, "BestToolService", StringComparison.OrdinalIgnoreCase), ConfigurationManager.AppSettings("connBTS").ToString(), ConfigurationManager.AppSettings("connBT").ToString())
            Using lookupCn As New OleDbConnection(lookupConnectionString)
                lookupCn.Open()
                Using descrCmd As New OleDbCommand("select ClientiSediDescrizione from ClientiSedi where ClientiSediCodice = ?", lookupCn)
                    descrCmd.Parameters.AddWithValue("@p1", clientiSediCodice)
                    Dim result = descrCmd.ExecuteScalar()
                    If result IsNot Nothing AndAlso Not Convert.IsDBNull(result) Then descrizione = result.ToString()
                End Using
            End Using

            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                Using cmd As New OleDbCommand("update TelegramBotWorkLocationResponses set ResponseType = 'CLIENTE_SEDE', ClientiSediCodice = ?, ClientiSediDescrizione = ?, RespondedAt = ? where TelegramBotWorkLocationResponseId = ? and ChatId = ?", cn)
                    cmd.Parameters.AddWithValue("@p1", clientiSediCodice)
                    cmd.Parameters.AddWithValue("@p2", descrizione)
                    cmd.Parameters.AddWithValue("@p3", Date.Now)
                    cmd.Parameters.AddWithValue("@p4", reminderId)
                    cmd.Parameters.AddWithValue("@p5", chatId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using

            SendTextMessage(chatId, "Grazie, ho registrato: " & If(String.IsNullOrEmpty(descrizione), clientiSediCodice, descrizione), "", "", "")
        End Sub

        Private Sub RichiediWorkLocationAltro(ByVal chatId As String, ByVal reminderId As String)
            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                Using cmd As New OleDbCommand("update TelegramBotWorkLocationResponses set ResponseType = 'ALTRO_ATTESA_TESTO' where TelegramBotWorkLocationResponseId = ? and ChatId = ?", cn)
                    cmd.Parameters.AddWithValue("@p1", reminderId)
                    cmd.Parameters.AddWithValue("@p2", chatId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using

            SendTextMessage(chatId, "Scrivi il cliente o la sede dove stai lavorando. Puoi usare la dettatura del telefono: mi arriverà come testo.", "", "", "")
        End Sub

        Private Function RegistraWorkLocationAltroDaTesto(ByVal chatId As String, ByVal text As String) As Boolean
            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                Using cmd As New OleDbCommand("select top 1 TelegramBotWorkLocationResponseId from TelegramBotWorkLocationResponses where ChatId = ? and ResponseType = 'ALTRO_ATTESA_TESTO' order by CreatedAt desc", cn)
                    cmd.Parameters.AddWithValue("@p1", chatId)
                    Dim reminderId = cmd.ExecuteScalar()
                    If reminderId Is Nothing OrElse Convert.IsDBNull(reminderId) Then Return False

                    Using updateCmd As New OleDbCommand("update TelegramBotWorkLocationResponses set ResponseType = 'ALTRO', [FreeText] = ?, RespondedAt = ? where TelegramBotWorkLocationResponseId = ?", cn)
                        updateCmd.Parameters.AddWithValue("@p1", text)
                        updateCmd.Parameters.AddWithValue("@p2", Date.Now)
                        updateCmd.Parameters.AddWithValue("@p3", reminderId)
                        updateCmd.ExecuteNonQuery()
                    End Using
                End Using
            End Using

            SendTextMessage(chatId, "Grazie, ho registrato: " & text, "", "", "")
            Return True
        End Function

        Private Function SendTimesheetReminderToAll(ByVal dataOraInvio As DateTime, Optional ByVal onlyChatId As String = "") As Integer
            Dim isTestMode = Not String.IsNullOrWhiteSpace(onlyChatId)
            If Not isTestMode Then
                Dim slots = GetOfficialTimesheetReminderSlots()
                If slots.Count = 0 Then Return 0

                Dim firstSlotTime = SlotToTimeSpan(slots(0))
                Dim lastSlotTime = SlotToTimeSpan(slots(slots.Count - 1)).Add(New TimeSpan(0, GetTimesheetReminderRetryUntilMinutes(), 59))
                If dataOraInvio.TimeOfDay < firstSlotTime OrElse dataOraInvio.TimeOfDay > lastSlotTime Then Return 0
            End If

            Dim cnBT As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
            If Not IsWorkDay(cnBT, dataOraInvio.Date) Then Return 0

            Dim sent As Integer = 0
            sent += SendTimesheetReminderForCompany("BestTool", ConfigurationManager.AppSettings("connBT").ToString(), dataOraInvio, onlyChatId)
            sent += SendTimesheetReminderForCompany("BestToolService", ConfigurationManager.AppSettings("connBTS").ToString(), dataOraInvio, onlyChatId)
            Return sent
        End Function

        Private Function SendTimesheetReminderForCompany(ByVal azienda As String, ByVal connectionString As String, ByVal dataOraInvio As DateTime, Optional ByVal onlyChatId As String = "") As Integer
            Dim sent As Integer = 0
            Dim isTestMode = Not String.IsNullOrWhiteSpace(onlyChatId)
            Dim currentSlot = GetCurrentTimesheetReminderSlot(dataOraInvio, isTestMode)
            Dim sql = "select '" & azienda.Replace("'", "''") & "' as Azienda, DeviceID, SoggettiCodice, " &
                      " iif(isnull(SoggettiSoprannome, '') = '', SoggettiNome, SoggettiSoprannome) as SoggettiSoprannome " &
                      " from Device inner join Utenti on UtentiID = DeviceUtenteID inner join Soggetti on SoggettiCodice = UtentiCodiceSoggetto " &
                      " where SoggettiStato <> 'A' and UtentiStato <> 'A' and DeviceStato <> 'A' and isnull(DeviceTelegram, '') = 'S' and isnull(DeviceID, '') <> '' " &
                      " and isnull(DeviceTelegramReminderRapportiniSN, 'S') <> 'N' "

            If Not String.IsNullOrWhiteSpace(onlyChatId) Then
                sql &= " and DeviceID = ? "
            End If

            Using cn As New OleDbConnection(connectionString)
                cn.Open()
                Using cmd As New OleDbCommand(sql, cn)
                    If Not String.IsNullOrWhiteSpace(onlyChatId) Then
                        cmd.Parameters.AddWithValue("@p1", onlyChatId)
                    End If

                    Using rs = cmd.ExecuteReader()
                        Do While rs.Read()
                            Dim chatId = rs.Item("DeviceID").ToString()
                            Dim soggettiCodice = rs.Item("SoggettiCodice").ToString()
                            Dim soprannome = rs.Item("SoggettiSoprannome").ToString()
                            Dim oreInserite = GetTimesheetHours(connectionString, soggettiCodice, dataOraInvio.Date)

                            If oreInserite < 8 Then
                                Dim reminderIds = GetPendingTimesheetReminderIds(chatId, dataOraInvio.Date, dataOraInvio)
                                If Not String.IsNullOrWhiteSpace(currentSlot) Then
                                    Dim currentReminderId = CreaTimesheetReminderLog(chatId, soggettiCodice, azienda, dataOraInvio.Date, currentSlot, oreInserite)
                                    If currentReminderId > 0 AndAlso Not reminderIds.Contains(currentReminderId) Then reminderIds.Add(currentReminderId)
                                End If

                                If reminderIds.Count > 0 Then
                                    Dim reminderId = reminderIds(0)
                                    Try
                                        Dim text = GetRandomTimesheetReminderText(soprannome, oreInserite)
                                        Dim keyboard = JsonConvert.SerializeObject(New With {.inline_keyboard = New Object() {New Object() {New With {.text = "Inserisci rapportino", .callback_data = "TS_START||" & reminderId.ToString()}}}})
                                        Dim messageId = SendTelegramMessageWithReplyMarkup(chatId, text, keyboard)
                                        AggiornaTimesheetReminderMessageId(reminderId, messageId)
                                        sent += 1
                                    Catch ex As Exception
                                        SegnaTimesheetReminderFallito(reminderId, ex.Message)
                                        ScriviLogBot("ERRORE invio reminder rapportino chat " & chatId & ": " & ex.Message)
                                    End Try
                                End If
                            End If
                        Loop
                    End Using
                End Using
            End Using

            Return sent
        End Function

        Private Function GetCurrentTimesheetReminderSlot(ByVal dataOraInvio As DateTime, ByVal isTestMode As Boolean) As String
            If isTestMode Then Return dataOraInvio.ToString("HHmm")

            Dim slot = dataOraInvio.ToString("HHmm")
            If GetOfficialTimesheetReminderSlots().Contains(slot) Then Return slot

            Return ""
        End Function

        Private Function GetOfficialTimesheetReminderSlots() As List(Of String)
            Dim configuredSlots = ConfigurationManager.AppSettings("TimesheetReminderSlots")
            If String.IsNullOrWhiteSpace(configuredSlots) Then configuredSlots = "1700,1730,1800,1830,1900"

            Dim slots As New List(Of String)
            For Each rawSlot In configuredSlots.Split(","c)
                Dim slot = rawSlot.Trim().Replace(":", "")
                If slot.Length = 4 Then
                    Dim hour As Integer
                    Dim minute As Integer
                    If Integer.TryParse(slot.Substring(0, 2), hour) AndAlso
                       Integer.TryParse(slot.Substring(2, 2), minute) AndAlso
                       hour >= 0 AndAlso hour <= 23 AndAlso minute >= 0 AndAlso minute <= 59 AndAlso
                       Not slots.Contains(slot) Then
                        slots.Add(slot)
                    End If
                End If
            Next

            slots.Sort()
            Return slots
        End Function

        Private Function GetTimesheetReminderRetryUntilMinutes() As Integer
            Dim configuredValue = ConfigurationManager.AppSettings("TimesheetReminderRetryUntilMinutes")
            Dim minutes As Integer
            If Integer.TryParse(configuredValue, minutes) AndAlso minutes >= 0 AndAlso minutes <= 59 Then Return minutes
            Return 29
        End Function

        Private Function SlotToTimeSpan(ByVal slot As String) As TimeSpan
            Return New TimeSpan(Convert.ToInt32(slot.Substring(0, 2)), Convert.ToInt32(slot.Substring(2, 2)), 0)
        End Function

        Private Function GetPendingTimesheetReminderIds(ByVal chatId As String, ByVal reminderDate As DateTime, ByVal dataOraInvio As DateTime) As List(Of Integer)
            Dim ids As New List(Of Integer)
            Dim officialSlots = GetOfficialTimesheetReminderSlots()

            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                Using cmd As New OleDbCommand("select TelegramBotTimesheetReminderId, ReminderSlot from TelegramBotTimesheetReminders where ChatId = ? and ReminderDate = ? and SentAt is null order by ReminderSlot", cn)
                    cmd.Parameters.AddWithValue("@p1", chatId)
                    cmd.Parameters.AddWithValue("@p2", reminderDate)
                    Using rs = cmd.ExecuteReader()
                        Do While rs.Read()
                            Dim slot = rs.Item("ReminderSlot").ToString()
                            If officialSlots.Contains(slot) AndAlso slot <= dataOraInvio.ToString("HHmm") Then
                                ids.Add(Convert.ToInt32(rs.Item("TelegramBotTimesheetReminderId")))
                            End If
                        Loop
                    End Using
                End Using
            End Using

            Return ids
        End Function

        Private Function GetTimesheetHours(ByVal connectionString As String, ByVal soggettiCodice As String, ByVal giorno As DateTime) As Decimal
            Using cn As New OleDbConnection(connectionString)
                cn.Open()
                Using cmd As New OleDbCommand("select isnull(sum(RapportiOreEseguite), 0) from Rapporti where RapportiStato <> 'A' and RapportiCodiceSoggetto = ? and RapportiData = ?", cn)
                    cmd.Parameters.AddWithValue("@p1", soggettiCodice)
                    cmd.Parameters.AddWithValue("@p2", giorno.ToString("yyyyMMdd"))
                    Dim result = cmd.ExecuteScalar()
                    If result Is Nothing OrElse Convert.IsDBNull(result) Then Return 0
                    Return Convert.ToDecimal(result)
                End Using
            End Using
        End Function

        Private Function CreaTimesheetReminderLog(ByVal chatId As String, ByVal soggettiCodice As String, ByVal azienda As String, ByVal reminderDate As DateTime, ByVal reminderSlot As String, ByVal oreInserite As Decimal) As Integer
            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                Using checkCmd As New OleDbCommand("select TelegramBotTimesheetReminderId, SentAt from TelegramBotTimesheetReminders where ChatId = ? and ReminderDate = ? and ReminderSlot = ?", cn)
                    checkCmd.Parameters.AddWithValue("@p1", chatId)
                    checkCmd.Parameters.AddWithValue("@p2", reminderDate)
                    checkCmd.Parameters.AddWithValue("@p3", reminderSlot)
                    Using rs = checkCmd.ExecuteReader()
                        If rs.Read() Then
                            If Not Convert.IsDBNull(rs.Item("SentAt")) Then Return 0
                            Return Convert.ToInt32(rs.Item("TelegramBotTimesheetReminderId"))
                        End If
                    End Using
                End Using

                Using cmd As New OleDbCommand("insert into TelegramBotTimesheetReminders (ChatId, SoggettiCodice, Azienda, ReminderDate, ReminderSlot, OreInserite, CreatedAt) values (?, ?, ?, ?, ?, ?, ?); select @@Identity", cn)
                    cmd.Parameters.AddWithValue("@p1", chatId)
                    cmd.Parameters.AddWithValue("@p2", soggettiCodice)
                    cmd.Parameters.AddWithValue("@p3", azienda)
                    cmd.Parameters.AddWithValue("@p4", reminderDate)
                    cmd.Parameters.AddWithValue("@p5", reminderSlot)
                    cmd.Parameters.AddWithValue("@p6", oreInserite)
                    cmd.Parameters.AddWithValue("@p7", Date.Now)
                    Return Convert.ToInt32(cmd.ExecuteScalar())
                End Using
            End Using
        End Function

        Private Sub AggiornaTimesheetReminderMessageId(ByVal reminderId As Integer, ByVal messageId As String)
            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                Using cmd As New OleDbCommand("update TelegramBotTimesheetReminders set TelegramMessageId = ?, SentAt = ?, SendFailedAt = null, LastSendError = null where TelegramBotTimesheetReminderId = ?", cn)
                    cmd.Parameters.AddWithValue("@p1", messageId)
                    cmd.Parameters.AddWithValue("@p2", Date.Now)
                    cmd.Parameters.AddWithValue("@p3", reminderId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        Private Sub SegnaTimesheetReminderFallito(ByVal reminderId As Integer, ByVal errore As String)
            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                Using cmd As New OleDbCommand("update TelegramBotTimesheetReminders set SendAttemptCount = isnull(SendAttemptCount, 0) + 1, SendFailedAt = ?, LastSendError = ? where TelegramBotTimesheetReminderId = ?", cn)
                    cmd.Parameters.AddWithValue("@p1", Date.Now)
                    cmd.Parameters.AddWithValue("@p2", If(errore.Length > 1000, errore.Substring(0, 1000), errore))
                    cmd.Parameters.AddWithValue("@p3", reminderId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        Private Function GetRandomTimesheetReminderText(ByVal soprannome As String, ByVal oreInserite As Decimal) As String
            Dim nome = If(String.IsNullOrWhiteSpace(soprannome), "utente", soprannome.Trim())
            Static rnd As New Random()
            Dim frasi = New String() {
                "`<user>`, se non mi fai lavorare, finisce che mi disattivano!! Eeemmandami sto RAPPORTINO che ti tolgo il pensiero.",
                "`<user>`? Ci sei? se ci sei batti un colpo che ti carico i `RAPPORTINI`",
                "`<user>`! Che la Forza sia con te. Mandami il `RAPPORTINO` !",
                "`<user>`, dopotutto, domani è un altro giorno. Mandami i `RAPPORTINI` di oggi!",
                "Gli farò un’offerta che non potrà rifiutare. Gli chiedo di inserire i `RAPPORTINI` di oggi!",
                "E.T. telefono casa. `<user>`, manda i `RAPPORTINI`",
                "Il mio nome è Catalog, BT Catalog! e aspetto i tuoi `RAPPORTINI` di oggi.",
                "`<user>`, se non mi mandi i `RAPPORTINI` di oggi, sei solo chiacchiere e distintivo!",
                "La vita è come una scatola di `RAPPORTINI`. Non sai mai quello che ti capita. Mandameli!",
                "Se io posso cambiare, e se voi potete cambiare, allora, tutto il mondo può cambiare! Da oggi fai i `RAPPORTINI` con Telegram! Mandamelo !!",
                "`<user>` ... metti la cera, togli la cera. Metti il `RAPPORTINO` e non togliere il `RAPPORTINO`.",
                "Il mattino ha l’oro in bocca, Il mattino ha l’oro in bocca, la sera btcatalog aspetta il `RAPPORTINO` !!",
                "Quando un uomo con la pistola incontra un uomo col fucile, quello che non ha inserito il `RAPPORTINO` è un uomo morto.",
                "Houston, abbiamo un problema! Mancano i tuoi `RAPPORTINI` di oggi ! Mandameli!",
                "Io ne ho viste cose che voi umani non potreste immaginarvi. Ma non vedo i tuoi `RAPPORTINI` !! Mandameli",
                "Avremo sempre Parigi... Ma non abbiamo mai i tuoi `RAPPORTINI`! Mandameli!",
                "Possono toglierci la vita, ma non ci toglieranno mai la libertà. RAPPORTINIIIIIII!!!",
                "Sono il Signor Wolf, risolvo problemi. Basta un messaggio e carico anche i tuoi `RAPPORTINI`",
                "Vedi, il mondo si divide in due categorie: chi inserisce i `RAPPORTINI` e chi no! Mandameli dai !! ",
                "`RAPPORTINI` ? Elementare, mio caro Watson-`<user>`.",
                "Specchio specchio delle mie brame, chi ha inserito i `RAPPORTINI` oggi? TU NO !! Mandameli che li carico x te",
                "Mi chiamo Massimo Decimo Meridio, comandante dell’esercito del Nord, generale delle legioni Felix, servo leale dell’unico vero imperatore BEST TOOL. Padre di un figlio che non carica i `RAPPORTINI`, marito di una moglie che non inserisce niente in agenda. E avrò la mia vendetta, in questa vita o nell’altra. Caricherò i `RAPPORTINI` ADESSO!!",
                "Nessun posto è bello come casa mia con dentro tutti i `RAPPORTINI` del giorno! Mandami il messaggio che ci penso io",
                "Non può piovere per sempre. Oggi inserirai i `RAPPORTINI` con Telegram !",
                "Maccherone m’hai provocato? …e io me te magno! Carica i `RAPPORTINI` con Telegram mangiando",
                "Al mio segnale, scatenate l'infermo ed inserite i `RAPPORTINI` con Telegram! Vai `<user>` ADESSO !! ",
                "`<user>`, ti farò un'offerta che non puoi rifiutare. Inserisci un `RAPPORTINO`!",
                "A Ventice', si nun scappa fori u `RAPPORTINO` so' uccelli aspri.",
                "Pronto, Ciampino? Sì. Mettetete stò `RAPPORTINO` và !",
                "AAAAAATTILA! A come atrocità, doppia T come terremoto e tragedia, I come ira di Dio L come LACO di sangue e A come adesso scrivo e ti mando il `RAPPORTINO` di oggi !! ",
                "Non c'è cattivo più cattivo di un buono quando diventa cattivo ed inserisce un `RAPPORTINO` di quanto fatto oggi !! ",
                "Senti, tu lo reggi il whisky? Be', i primi due galloni sì, al terzo divento nostalgico e ti mando i `RAPPORTINI` di oggi!!",
                "Oh, ma ce l'hai con me? Perchè non mi mandi il RAPPORTINO? Dai su!!",
                "Ti prego, non è colpa mia, è il mio lavoro ... Mandalooooo...",
                "Aiutooooo, non mi hai mandato il RAPPORTINO... qui finisce me mi buttano fuori!",
                "Soccia dai... mandalo anche alla vecchia, che lo carico io in camuffa!",
                "A ru cavallu jestimatu luce ru pilu (Al cavallo che riceve imprecazioni luccica il pelo.), quindi è meglio se mi mandi il RAPPORTINO se no finisce male...",
                "Ogni scarrafone è bello 'a mamma soja, ma se mandi il `RAPPORTINO` forse è megilo.",
                "Tanto va la gatta al lardo che ci lascia il `RAPPORTINO`",
                "Sopra la panca la capra canta, sotto la panca la gatta mette il `RAPPORTINO`",
                "A caval donato non si manda in bocca ... il `RAPPORTINO`.",
                "A buon intenditor, poche parole. ... quindi a me basta poco!",
                "Fra i due litiganti il `RAPPORTINO` gode. ... ",
                "Il buongiorno si vede dal mattino. ...se mi mandi il `RAPPORTINO`",
                "Ride bene chi lo manda per ultimo... ma lo manda!",
                "Chi dorme ... non piglia il `RAPPORTINO`! ",
                "Chi non risica `RAPPORTINI` non rosica `RAPPORTINI`",
                "Tra moglie e marito non mettere il `RAPPORTINO`!",
                "L’erba del vicino è sempre più verde se mi mandi il `RAPPORTINO`.",
                "Il `RAPPORTINO` vien parlando!",
                "Piove sempre sul rapprtino!",
                "Occhio non vede, cuore non duole, il `RAPPORTINO` il bot vuole!",
                "Piove, piove, la gatte fa le ove, ti dice buonasera e mi mandi il `RAPPORTINO`!",
                "Ambasciator non porta pena, se il `RAPPORTINO` è una pena"
            }

            Dim frase = frasi(rnd.Next(frasi.Length)).
                Replace("`<user>`", nome).
                Replace("<user>", nome).
                Replace("`", "")

            Return frase
        End Function

        Private Sub StartTimesheetDraft(ByVal chatId As String)
            Dim AUT00 = VerificaAutorizzazioni(chatId)
            If AUT00.Rows.Count = 0 Then
                SendTextMessage(chatId, "Utente non registrato.", "", "", "")
                Exit Sub
            End If

            Dim soggettiCodice = AUT00.Rows(0).Item("SoggettiCodice").ToString()
            Dim azienda = AUT00.Rows(0).Item("Azienda").ToString()
            StartTimesheetDraftForUser(chatId, soggettiCodice, azienda)
        End Sub

        Private Sub StartTimesheetDraftFromReminder(ByVal chatId As String, ByVal reminderId As String)
            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                Using cmd As New OleDbCommand("select SoggettiCodice, Azienda from TelegramBotTimesheetReminders where TelegramBotTimesheetReminderId = ? and ChatId = ?", cn)
                    cmd.Parameters.AddWithValue("@p1", reminderId)
                    cmd.Parameters.AddWithValue("@p2", chatId)
                    Using rs = cmd.ExecuteReader()
                        If rs.Read() Then
                            StartTimesheetDraftForUser(chatId, rs.Item("SoggettiCodice").ToString(), rs.Item("Azienda").ToString())
                            Exit Sub
                        End If
                    End Using
                End Using
            End Using

            StartTimesheetDraft(chatId)
        End Sub

        Private Sub StartTimesheetDraftForUser(ByVal chatId As String, ByVal soggettiCodice As String, ByVal azienda As String)
            Dim draftId = CreateTimesheetDraft(chatId, soggettiCodice, azienda)
            SendTimesheetDateChoices(chatId, draftId.ToString())
        End Sub

        Private Sub SendTimesheetDateChoices(ByVal chatId As String, ByVal draftId As String)
            Dim today = Date.Today
            Dim yesterday = Date.Today.AddDays(-1)
            Dim twoDaysAgo = Date.Today.AddDays(-2)
            Dim keyboard = JsonConvert.SerializeObject(New With {
                .inline_keyboard = New Object() {
                    New Object() {New With {.text = "Oggi (" & today.ToString("dd/MM") & ")", .callback_data = "TS_DATE||" & draftId & "||" & today.ToString("yyyyMMdd")}},
                    New Object() {New With {.text = "Ieri (" & yesterday.ToString("dd/MM") & ")", .callback_data = "TS_DATE||" & draftId & "||" & yesterday.ToString("yyyyMMdd")}},
                    New Object() {New With {.text = "Due giorni fa (" & twoDaysAgo.ToString("dd/MM") & ")", .callback_data = "TS_DATE||" & draftId & "||" & twoDaysAgo.ToString("yyyyMMdd")}},
                    New Object() {New With {.text = "Altra data...", .callback_data = "TS_DATE_OTHER||" & draftId}},
                    New Object() {New With {.text = "Non sono al lavoro", .callback_data = "TS_NOT_WORKING||" & draftId}},
                    BuildTimesheetCancelButtonRow(draftId)
                }
            })

            SendTimesheetMessageWithReplyMarkup(chatId, "Scegli la data del rapportino.", keyboard)
        End Sub

        Private Function BuildTimesheetCancelButtonRow(ByVal draftId As String) As Object()
            Return New Object() {New With {.text = "Annulla inserimento rapportino", .callback_data = "TS_CANCEL||" & draftId}}
        End Function

        Private Sub AddTimesheetCancelButton(ByVal keyboard As List(Of Object), ByVal draftId As String)
            keyboard.Add(New List(Of Object) From {New With {.text = "Annulla inserimento rapportino", .callback_data = "TS_CANCEL||" & draftId}})
        End Sub

        Private Function CreateTimesheetDraft(ByVal chatId As String, ByVal soggettiCodice As String, ByVal azienda As String) As Object
            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                Using cmd As New OleDbCommand("insert into TelegramBotTimesheetDrafts (ChatId, SoggettiCodice, Azienda, Step, RapportiData, CreatedAt, UpdatedAt) values (?, ?, ?, 'CLIENTE', ?, ?, ?); select @@Identity", cn)
                    cmd.Parameters.AddWithValue("@p1", chatId)
                    cmd.Parameters.AddWithValue("@p2", soggettiCodice)
                    cmd.Parameters.AddWithValue("@p3", azienda)
                    cmd.Parameters.AddWithValue("@p4", Date.Today)
                    cmd.Parameters.AddWithValue("@p5", Date.Now)
                    cmd.Parameters.AddWithValue("@p6", Date.Now)
                    Return cmd.ExecuteScalar()
                End Using
            End Using
        End Function

        Private Sub TimesheetSetDate(ByVal chatId As String, ByVal draftId As String, ByVal dataValue As String)
            Dim rapportiData As DateTime
            If Not DateTime.TryParseExact(dataValue, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, rapportiData) Then
                SendTextMessage(chatId, "Data non valida. Usa il formato gg/mm/aaaa.", "", "", "")
                Exit Sub
            End If

            UpdateTimesheetDraftField(draftId, chatId, "RapportiData", rapportiData, "CLIENTE")
            SendTimesheetClientChoices(chatId, draftId)
        End Sub

        Private Sub TimesheetAskCustomDate(ByVal chatId As String, ByVal draftId As String)
            UpdateTimesheetDraftStep(draftId, chatId, "WAIT_DATE")
            Dim keyboard = JsonConvert.SerializeObject(New With {.inline_keyboard = New Object() {BuildTimesheetCancelButtonRow(draftId)}})
            SendTimesheetMessageWithReplyMarkup(chatId, "Scrivi la data del rapportino nel formato gg/mm/aaaa.", keyboard)
        End Sub

        Private Sub TimesheetAskReturnDate(ByVal chatId As String, ByVal draftId As String)
            UpdateTimesheetDraftField(draftId, chatId, "RapportiData", Date.Today, "RETURN_DATE")
            SendTimesheetReturnDateChoices(chatId, draftId)
        End Sub

        Private Sub SendTimesheetReturnDateChoices(ByVal chatId As String, ByVal draftId As String)
            Dim tomorrow = Date.Today.AddDays(1)
            Dim keyboard = JsonConvert.SerializeObject(New With {
                .inline_keyboard = New Object() {
                    New Object() {New With {.text = "Domani (" & tomorrow.ToString("dd/MM") & ")", .callback_data = "TS_RETURN_DATE||" & draftId & "||" & tomorrow.ToString("yyyyMMdd")}},
                    New Object() {New With {.text = "Altra data...", .callback_data = "TS_RETURN_OTHER||" & draftId}},
                    BuildTimesheetCancelButtonRow(draftId)
                }
            })

            SendTimesheetMessageWithReplyMarkup(chatId, "Che giorno torni?", keyboard)
        End Sub

        Private Sub TimesheetAskCustomReturnDate(ByVal chatId As String, ByVal draftId As String)
            UpdateTimesheetDraftStep(draftId, chatId, "WAIT_RETURN_DATE")
            Dim keyboard = JsonConvert.SerializeObject(New With {.inline_keyboard = New Object() {BuildTimesheetCancelButtonRow(draftId)}})
            SendTimesheetMessageWithReplyMarkup(chatId, "Scrivi il giorno di rientro nel formato gg/mm/aaaa.", keyboard)
        End Sub

        Private Sub TimesheetSetReturnDate(ByVal chatId As String, ByVal draftId As String, ByVal dataValue As String)
            Dim returnDate As DateTime
            If Not DateTime.TryParseExact(dataValue, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, returnDate) Then
                SendTimesheetReturnDateChoices(chatId, draftId)
                Exit Sub
            End If

            SaveNotWorkingReports(chatId, draftId, returnDate)
        End Sub

        Private Sub SendTimesheetClientChoices(ByVal chatId As String, ByVal draftId As String)
            Dim draft = GetTimesheetDraft(draftId, chatId)
            If draft.Rows.Count = 0 Then Exit Sub

            Dim azienda = draft.Rows(0).Item("Azienda").ToString()
            Dim soggettiCodice = draft.Rows(0).Item("SoggettiCodice").ToString()
            Dim connectionString = GetCompanyConnectionString(azienda)
            Dim keyboard As New List(Of Object)

            If LoadTimesheetClientChoicesFromCache(keyboard, draftId, azienda, soggettiCodice) = 0 Then
                LoadTimesheetClientChoicesFromReports(keyboard, draftId, connectionString, soggettiCodice)
            End If

            keyboard.Add(New List(Of Object) From {New With {.text = "Altro cliente...", .callback_data = "TS_CLIENT_SEARCH||" & draftId}})
            AddTimesheetCancelButton(keyboard, draftId)

            Dim dataRapporto = Convert.ToDateTime(draft.Rows(0).Item("RapportiData")).ToString("dd/MM/yyyy")
            SendTimesheetMessageWithReplyMarkup(chatId, "Data rapportino: " & dataRapporto & ". Scegli il cliente.", JsonConvert.SerializeObject(New With {.inline_keyboard = keyboard}))
        End Sub

        Private Function LoadTimesheetClientChoicesFromCache(ByVal keyboard As List(Of Object), ByVal draftId As String, ByVal azienda As String, ByVal soggettiCodice As String) As Integer
            Dim count = 0
            Dim sql = "select top 5 ClientiCodice, ClientiRagioneSociale from TelegramBotRecentClientsCache " &
                      " where Azienda = ? and SoggettiCodice = ? " &
                      " order by DataUltimoIntervento desc, UltimoRapportiCodice desc"

            Try
                Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                    cn.Open()
                    Using cmd As New OleDbCommand(sql, cn)
                        cmd.Parameters.AddWithValue("@p1", azienda)
                        cmd.Parameters.AddWithValue("@p2", soggettiCodice)
                        Using rs = cmd.ExecuteReader()
                            Do While rs.Read()
                                AddTimesheetClientChoice(keyboard, draftId, rs.Item("ClientiCodice").ToString(), rs.Item("ClientiRagioneSociale").ToString())
                                count += 1
                            Loop
                        End Using
                    End Using
                End Using
            Catch ex As Exception
                ScriviLogBot("ERRORE lettura TelegramBotRecentClientsCache: " & ex.Message)
                Return 0
            End Try

            Return count
        End Function

        Private Sub LoadTimesheetClientChoicesFromReports(ByVal keyboard As List(Of Object), ByVal draftId As String, ByVal connectionString As String, ByVal soggettiCodice As String)
            Dim sql = "select TOP 5 C.ClientiCodice, C.ClientiRagioneSociale, max(R.RapportiData) as DataUltimoIntervento, max(R.RapportiCodice) as UltimoRapportiCodice " &
                      " from Rapporti R inner join Clienti C on C.ClientiCodice = R.RapportiCodiceCliente " &
                      " where R.RapportiStato <> 'A' and R.RapportiData >= '20210101' and R.RapportiCodiceSoggetto = ? and C.ClientiCodice <> 99999 and C.ClientiStato <> 'A' " &
                      " group by C.ClientiCodice, C.ClientiRagioneSociale order by DataUltimoIntervento desc, UltimoRapportiCodice desc "

            Using cn As New OleDbConnection(connectionString)
                cn.Open()
                Using cmd As New OleDbCommand(sql, cn)
                    cmd.Parameters.AddWithValue("@p1", soggettiCodice)
                    Using rs = cmd.ExecuteReader()
                        Do While rs.Read()
                            AddTimesheetClientChoice(keyboard, draftId, rs.Item("ClientiCodice").ToString(), rs.Item("ClientiRagioneSociale").ToString())
                        Loop
                    End Using
                End Using
            End Using
        End Sub

        Private Sub AddTimesheetClientChoice(ByVal keyboard As List(Of Object), ByVal draftId As String, ByVal clientiCodice As String, ByVal clientiRagioneSociale As String)
            Dim row As New List(Of Object)
            row.Add(New With {.text = clientiRagioneSociale, .callback_data = "TS_CLIENT||" & draftId & "||" & clientiCodice})
            keyboard.Add(row)
        End Sub

        Private Sub TimesheetAskClientSearch(ByVal chatId As String, ByVal draftId As String)
            UpdateTimesheetDraftStep(draftId, chatId, "WAIT_CLIENT_SEARCH")
            Dim keyboard = JsonConvert.SerializeObject(New With {.inline_keyboard = New Object() {BuildTimesheetCancelButtonRow(draftId)}})
            SendTimesheetMessageWithReplyMarkup(chatId, "Scrivi una parte del nome cliente.", keyboard)
        End Sub

        Private Sub TimesheetSetClient(ByVal chatId As String, ByVal draftId As String, ByVal clientiCodice As String)
            UpdateTimesheetDraftField(draftId, chatId, "ClientiCodice", clientiCodice, "SEDE")
            SendTimesheetSiteChoices(chatId, draftId)
        End Sub

        Private Sub SendTimesheetSiteChoices(ByVal chatId As String, ByVal draftId As String)
            Dim draft = GetTimesheetDraft(draftId, chatId)
            If draft.Rows.Count = 0 Then Exit Sub

            Dim azienda = draft.Rows(0).Item("Azienda").ToString()
            Dim clientiCodice = draft.Rows(0).Item("ClientiCodice").ToString()
            Dim connectionString = GetCompanyConnectionString(azienda)
            Dim keyboard As New List(Of Object)
            Dim sql = "select ClientiSediCodice, ClientiSediDescrizione from ClientiSedi where ClientiSediCodiceCliente = ? and ClientiSediStato <> 'A' order by ClientiSediDescrizione"

            Using cn As New OleDbConnection(connectionString)
                cn.Open()
                Using cmd As New OleDbCommand(sql, cn)
                    cmd.Parameters.AddWithValue("@p1", clientiCodice)
                    Using rs = cmd.ExecuteReader()
                        Do While rs.Read()
                            Dim row As New List(Of Object)
                            row.Add(New With {.text = rs.Item("ClientiSediDescrizione").ToString(), .callback_data = "TS_SITE||" & draftId & "||" & rs.Item("ClientiSediCodice").ToString()})
                            keyboard.Add(row)
                        Loop
                    End Using
                End Using
            End Using

            If keyboard.Count = 0 Then
                Dim cancelKeyboard = JsonConvert.SerializeObject(New With {.inline_keyboard = New Object() {BuildTimesheetCancelButtonRow(draftId)}})
                SendTimesheetMessageWithReplyMarkup(chatId, "Non ho trovato sedi per questo cliente.", cancelKeyboard)
                Exit Sub
            End If

            AddTimesheetCancelButton(keyboard, draftId)
            SendTimesheetMessageWithReplyMarkup(chatId, "Scegli la sede.", JsonConvert.SerializeObject(New With {.inline_keyboard = keyboard}))
        End Sub

        Private Sub TimesheetSetSite(ByVal chatId As String, ByVal draftId As String, ByVal clientiSediCodice As String)
            UpdateTimesheetDraftField(draftId, chatId, "ClientiSediCodice", clientiSediCodice, "ORE")

            Dim keyboard As New List(Of Object)
            For i = 1 To 12
                If (i - 1) Mod 4 = 0 Then keyboard.Add(New List(Of Object))
                CType(keyboard(keyboard.Count - 1), List(Of Object)).Add(New With {.text = i.ToString(), .callback_data = "TS_HOURS||" & draftId & "||" & i.ToString()})
            Next

            AddTimesheetCancelButton(keyboard, draftId)
            SendTimesheetMessageWithReplyMarkup(chatId, "Quante ore vuoi inserire?", JsonConvert.SerializeObject(New With {.inline_keyboard = keyboard}))
        End Sub

        Private Sub TimesheetSetHours(ByVal chatId As String, ByVal draftId As String, ByVal ore As String)
            Dim oreInt As Integer
            If Not Integer.TryParse(ore, oreInt) OrElse oreInt < 1 OrElse oreInt > 12 Then
                SendTextMessage(chatId, "Inserisci solo numeri interi da 1 a 12.", "", "", "")
                Exit Sub
            End If

            UpdateTimesheetDraftField(draftId, chatId, "Ore", oreInt, "WAIT_TEXT")
            Dim keyboard = JsonConvert.SerializeObject(New With {.inline_keyboard = New Object() {BuildTimesheetCancelButtonRow(draftId)}})
            SendTimesheetMessageWithReplyMarkup(chatId, "Scrivi il testo dell'attivita. Puoi digitarlo oppure usare la dettatura del telefono.", keyboard)
        End Sub

        Private Function HandleTimesheetText(ByVal chatId As String, ByVal text As String) As Boolean
            Dim draft = GetActiveTimesheetDraft(chatId)
            If draft.Rows.Count = 0 Then Return False

            Dim draftId = draft.Rows(0).Item("TelegramBotTimesheetDraftId").ToString()
            Dim stepName = draft.Rows(0).Item("Step").ToString()

            Select Case stepName
                Case "WAIT_DATE"
                    Dim parsed As DateTime
                    If Not DateTime.TryParseExact(text.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, parsed) Then
                        Dim keyboard = JsonConvert.SerializeObject(New With {.inline_keyboard = New Object() {BuildTimesheetCancelButtonRow(draftId)}})
                        SendTimesheetMessageWithReplyMarkup(chatId, "Data non valida. Usa il formato gg/mm/aaaa.", keyboard)
                        Return True
                    End If

                    UpdateTimesheetDraftField(draftId, chatId, "RapportiData", parsed, "CLIENTE")
                    SendTimesheetClientChoices(chatId, draftId)
                    Return True
                Case "WAIT_RETURN_DATE"
                    Dim parsed As DateTime
                    If Not DateTime.TryParseExact(text.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, parsed) Then
                        Dim keyboard = JsonConvert.SerializeObject(New With {.inline_keyboard = New Object() {BuildTimesheetCancelButtonRow(draftId)}})
                        SendTimesheetMessageWithReplyMarkup(chatId, "Data non valida. Usa il formato gg/mm/aaaa.", keyboard)
                        Return True
                    End If

                    SaveNotWorkingReports(chatId, draftId, parsed)
                    Return True
                Case "WAIT_CLIENT_SEARCH"
                    SendTimesheetClientSearchResults(chatId, draftId, text.Trim())
                    Return True
                Case "WAIT_TEXT"
                    UpdateTimesheetDraftField(draftId, chatId, "Testo", text.Trim(), "READY")
                    SaveTimesheetDraft(chatId, draftId)
                    Return True
            End Select

            Return False
        End Function

        Private Sub SendTimesheetClientSearchResults(ByVal chatId As String, ByVal draftId As String, ByVal searchText As String)
            Dim draft = GetTimesheetDraft(draftId, chatId)
            If draft.Rows.Count = 0 Then Exit Sub

            Dim azienda = draft.Rows(0).Item("Azienda").ToString()
            Dim connectionString = GetCompanyConnectionString(azienda)
            Dim keyboard As New List(Of Object)

            Using cn As New OleDbConnection(connectionString)
                cn.Open()
                Using cmd As New OleDbCommand("select TOP 10 ClientiCodice, ClientiRagioneSociale from Clienti where ClientiStato <> 'A' and ClientiRagioneSociale like ? order by ClientiRagioneSociale", cn)
                    cmd.Parameters.AddWithValue("@p1", "%" & searchText & "%")
                    Using rs = cmd.ExecuteReader()
                        Do While rs.Read()
                            keyboard.Add(New List(Of Object) From {New With {.text = rs.Item("ClientiRagioneSociale").ToString(), .callback_data = "TS_CLIENT||" & draftId & "||" & rs.Item("ClientiCodice").ToString()}})
                        Loop
                    End Using
                End Using
            End Using

            If keyboard.Count = 0 Then
                Dim cancelKeyboard = JsonConvert.SerializeObject(New With {.inline_keyboard = New Object() {BuildTimesheetCancelButtonRow(draftId)}})
                SendTimesheetMessageWithReplyMarkup(chatId, "Nessun cliente trovato. Prova con un'altra parola.", cancelKeyboard)
                Exit Sub
            End If

            AddTimesheetCancelButton(keyboard, draftId)
            SendTimesheetMessageWithReplyMarkup(chatId, "Seleziona il cliente trovato.", JsonConvert.SerializeObject(New With {.inline_keyboard = keyboard}))
        End Sub

        Private Sub SaveTimesheetDraft(ByVal chatId As String, ByVal draftId As String)
            Dim draft = GetTimesheetDraft(draftId, chatId)
            If draft.Rows.Count = 0 Then Exit Sub

            Try
                Dim azienda = draft.Rows(0).Item("Azienda").ToString()
                Dim connectionString = GetCompanyConnectionString(azienda)
                Dim rapportiCodice = InsertTimesheetReport(connectionString, draft.Rows(0))

                UpdateTimesheetDraftStep(draftId, chatId, "DONE")
                Dim keyboard = JsonConvert.SerializeObject(New With {.inline_keyboard = New Object() {New Object() {New With {.text = "Inserisci altro rapportino", .callback_data = "TS_MORE"}}}})
                SendTelegramMessageWithReplyMarkup(chatId, "Rapportino inserito. Codice: " & rapportiCodice.ToString(), keyboard)
            Catch ex As Exception
                SendTextMessage(chatId, "Non sono riuscito a inserire il rapportino: " & ex.Message, "", "", "")
            End Try
        End Sub

        Private Sub SaveNotWorkingReports(ByVal chatId As String, ByVal draftId As String, ByVal returnDate As DateTime)
            Dim draft = GetTimesheetDraft(draftId, chatId)
            If draft.Rows.Count = 0 Then Exit Sub

            Try
                Dim startDate = Convert.ToDateTime(draft.Rows(0).Item("RapportiData")).Date
                returnDate = returnDate.Date

                If returnDate <= startDate Then
                    SendTextMessage(chatId, "La data di rientro deve essere successiva alla data di inizio assenza.", "", "", "")
                    SendTimesheetReturnDateChoices(chatId, draftId)
                    Exit Sub
                End If

                Dim azienda = draft.Rows(0).Item("Azienda").ToString()
                Dim connectionString = GetCompanyConnectionString(azienda)
                Dim inserted As Integer = 0
                Dim currentDate = startDate

                Using workDayConnection As New OleDbConnection(connectionString)
                    Do While currentDate < returnDate
                        If IsWorkDay(workDayConnection, currentDate) Then
                            InsertNotWorkingReport(connectionString, draft.Rows(0), currentDate)
                            inserted += 1
                        End If

                        currentDate = currentDate.AddDays(1)
                    Loop
                End Using

                UpdateTimesheetDraftStep(draftId, chatId, "DONE")
                SendTextMessage(chatId, "Ho inserito " & inserted.ToString() & " rapportini ""Non al lavoro"".", "", "", "")
            Catch ex As Exception
                SendTextMessage(chatId, "Non sono riuscito a inserire i rapportini ""Non al lavoro"": " & ex.Message, "", "", "")
            End Try
        End Sub

        Private Function InsertNotWorkingReport(ByVal connectionString As String, ByVal draft As DataRow, ByVal reportDate As DateTime) As Integer
            Using cn As New OleDbConnection(connectionString)
                cn.Open()
                Dim rapportiCodice As Integer
                Using maxCmd As New OleDbCommand("select isnull(max(RapportiCodice), 0) + 1 from Rapporti", cn)
                    rapportiCodice = Convert.ToInt32(maxCmd.ExecuteScalar())
                End Using

                Dim sql = "insert into Rapporti (RapportiCodice, RapportiStato, RapportiCodiceCliente, RapportiCodiceClienteSede, RapportiCodiceSoggetto, RapportiData, RapportiOreEseguite, RapportiTitolo, RapportiDescrizioneAttivita, RapportiAlertSN, RapportiDUM, RapportiUDUM, RapportiOreFatturate, RapportiDescrizioneAttivitaInFattura, RapportiFattureRID, RapportiFattureRIDRiga, RapportiOra1Da, RapportiOra1A, RapportiOra2Da, RapportiOra2A, RapportiFatturareSN, RapportiDefaultFatturazione, RapportiValutazione) " &
                          " values (?, 'M', 99999, 99999, ?, ?, 8, 'Telegram', 'Non al lavoro', '', ?, ?, 8, '', '0', '0', '', '', '', '', '', '', '0')"

                Using cmd As New OleDbCommand(sql, cn)
                    cmd.Parameters.AddWithValue("@p1", rapportiCodice)
                    cmd.Parameters.AddWithValue("@p2", draft.Item("SoggettiCodice"))
                    cmd.Parameters.AddWithValue("@p3", reportDate.ToString("yyyyMMdd"))
                    cmd.Parameters.AddWithValue("@p4", Date.Now.ToString("yyyyMMdd HH:mm:ss"))
                    cmd.Parameters.AddWithValue("@p5", draft.Item("ChatId").ToString())
                    cmd.ExecuteNonQuery()
                End Using

                Return rapportiCodice
            End Using
        End Function

        Private Function InsertTimesheetReport(ByVal connectionString As String, ByVal draft As DataRow) As Integer
            Using cn As New OleDbConnection(connectionString)
                cn.Open()
                Dim rapportiCodice As Integer
                Using maxCmd As New OleDbCommand("select isnull(max(RapportiCodice), 0) + 1 from Rapporti", cn)
                    rapportiCodice = Convert.ToInt32(maxCmd.ExecuteScalar())
                End Using

                Dim sql = "insert into Rapporti (RapportiCodice, RapportiStato, RapportiCodiceCliente, RapportiCodiceClienteSede, RapportiCodiceSoggetto, RapportiData, RapportiOreEseguite, RapportiTitolo, RapportiDescrizioneAttivita, RapportiAlertSN, RapportiDUM, RapportiUDUM, RapportiOreFatturate, RapportiDescrizioneAttivitaInFattura, RapportiFattureRID, RapportiFattureRIDRiga, RapportiOra1Da, RapportiOra1A, RapportiOra2Da, RapportiOra2A, RapportiFatturareSN, RapportiDefaultFatturazione, RapportiValutazione) " &
                          " values (?, 'M', ?, ?, ?, ?, ?, 'Telegram', ?, '', ?, ?, ?, '', '0', '0', '', '', '', '', '', '', '0')"

                Using cmd As New OleDbCommand(sql, cn)
                    cmd.Parameters.AddWithValue("@p1", rapportiCodice)
                    cmd.Parameters.AddWithValue("@p2", draft.Item("ClientiCodice"))
                    cmd.Parameters.AddWithValue("@p3", draft.Item("ClientiSediCodice"))
                    cmd.Parameters.AddWithValue("@p4", draft.Item("SoggettiCodice"))
                    cmd.Parameters.AddWithValue("@p5", Convert.ToDateTime(draft.Item("RapportiData")).ToString("yyyyMMdd"))
                    cmd.Parameters.AddWithValue("@p6", draft.Item("Ore"))
                    cmd.Parameters.AddWithValue("@p7", draft.Item("Testo").ToString())
                    cmd.Parameters.AddWithValue("@p8", Date.Now.ToString("yyyyMMdd HH:mm:ss"))
                    cmd.Parameters.AddWithValue("@p9", draft.Item("ChatId").ToString())
                    cmd.Parameters.AddWithValue("@p10", draft.Item("Ore"))
                    cmd.ExecuteNonQuery()
                End Using

                Return rapportiCodice
            End Using
        End Function

        Private Function GetCompanyConnectionString(ByVal azienda As String) As String
            If String.Equals(azienda, "BestToolService", StringComparison.OrdinalIgnoreCase) Then
                Return ConfigurationManager.AppSettings("connBTS").ToString()
            End If

            Return ConfigurationManager.AppSettings("connBT").ToString()
        End Function

        Private Function GetTimesheetDraft(ByVal draftId As String, ByVal chatId As String) As DataTable
            Dim table As New DataTable()
            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                Using cmd As New OleDbCommand("select * from TelegramBotTimesheetDrafts where TelegramBotTimesheetDraftId = ? and ChatId = ?", cn)
                    cmd.Parameters.AddWithValue("@p1", draftId)
                    cmd.Parameters.AddWithValue("@p2", chatId)
                    Using adapter As New OleDbDataAdapter(cmd)
                        adapter.Fill(table)
                    End Using
                End Using
            End Using
            Return table
        End Function

        Private Function GetActiveTimesheetDraft(ByVal chatId As String) As DataTable
            Dim table As New DataTable()
            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                Using cmd As New OleDbCommand("select top 1 * from TelegramBotTimesheetDrafts where ChatId = ? and Step in ('WAIT_DATE', 'WAIT_RETURN_DATE', 'WAIT_CLIENT_SEARCH', 'WAIT_TEXT') order by UpdatedAt desc", cn)
                    cmd.Parameters.AddWithValue("@p1", chatId)
                    Using adapter As New OleDbDataAdapter(cmd)
                        adapter.Fill(table)
                    End Using
                End Using
            End Using
            Return table
        End Function

        Private Function GetOpenTimesheetDraft(ByVal chatId As String) As DataTable
            Dim table As New DataTable()
            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                Using cmd As New OleDbCommand("select top 1 * from TelegramBotTimesheetDrafts where ChatId = ? and Step not in ('DONE', 'CANCELLED') order by UpdatedAt desc", cn)
                    cmd.Parameters.AddWithValue("@p1", chatId)
                    Using adapter As New OleDbDataAdapter(cmd)
                        adapter.Fill(table)
                    End Using
                End Using
            End Using
            Return table
        End Function

        Private Function IsTimesheetDraftClosed(ByVal chatId As String, ByVal draftId As String) As Boolean
            Dim draft = GetTimesheetDraft(draftId, chatId)
            If draft.Rows.Count = 0 Then Return True

            Dim stepName = draft.Rows(0).Item("Step").ToString()
            Return String.Equals(stepName, "DONE", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(stepName, "CANCELLED", StringComparison.OrdinalIgnoreCase)
        End Function

        Private Sub CancelTimesheetDraft(ByVal chatId As String, ByVal draftId As String)
            Dim draft = GetTimesheetDraft(draftId, chatId)
            If draft.Rows.Count = 0 Then
                SendTextMessage(chatId, "Non ho trovato il rapportino da annullare.", "", "", "")
                Exit Sub
            End If

            Dim stepName = draft.Rows(0).Item("Step").ToString()
            If String.Equals(stepName, "DONE", StringComparison.OrdinalIgnoreCase) Then
                SendTextMessage(chatId, "Questo rapportino e gia stato inserito.", "", "", "")
                Exit Sub
            End If

            If String.Equals(stepName, "CANCELLED", StringComparison.OrdinalIgnoreCase) Then
                Exit Sub
            End If

            UpdateTimesheetDraftStep(draftId, chatId, "CANCELLED")
            SendTextMessage(chatId, "Inserimento rapportino annullato.", "", "", "")
        End Sub

        Private Function CancelActiveTimesheetDraft(ByVal chatId As String) As Boolean
            Dim draft = GetOpenTimesheetDraft(chatId)
            If draft.Rows.Count = 0 Then Return False

            CancelTimesheetDraft(chatId, draft.Rows(0).Item("TelegramBotTimesheetDraftId").ToString())
            Return True
        End Function

        Private Sub UpdateTimesheetDraftStep(ByVal draftId As String, ByVal chatId As String, ByVal stepName As String)
            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                Using cmd As New OleDbCommand("update TelegramBotTimesheetDrafts set Step = ?, UpdatedAt = ? where TelegramBotTimesheetDraftId = ? and ChatId = ?", cn)
                    cmd.Parameters.AddWithValue("@p1", stepName)
                    cmd.Parameters.AddWithValue("@p2", Date.Now)
                    cmd.Parameters.AddWithValue("@p3", draftId)
                    cmd.Parameters.AddWithValue("@p4", chatId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        Private Sub UpdateTimesheetDraftField(ByVal draftId As String, ByVal chatId As String, ByVal fieldName As String, ByVal fieldValue As Object, ByVal stepName As String)
            Dim allowedFields = New List(Of String) From {"RapportiData", "ClientiCodice", "ClientiSediCodice", "Ore", "Testo"}
            If Not allowedFields.Contains(fieldName) Then Throw New InvalidOperationException("Campo non valido")

            Using cn As New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                cn.Open()
                Using cmd As New OleDbCommand("update TelegramBotTimesheetDrafts set " & fieldName & " = ?, Step = ?, UpdatedAt = ? where TelegramBotTimesheetDraftId = ? and ChatId = ?", cn)
                    cmd.Parameters.AddWithValue("@p1", fieldValue)
                    cmd.Parameters.AddWithValue("@p2", stepName)
                    cmd.Parameters.AddWithValue("@p3", Date.Now)
                    cmd.Parameters.AddWithValue("@p4", draftId)
                    cmd.Parameters.AddWithValue("@p5", chatId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        Private Function ProattivoAggiungiAgenda(ByVal chatId As String, ByVal text As String, ByVal buttonText1 As String, ByVal buttonText2 As String, ByVal buttonText3 As String, ByVal buttonText4 As String, ByVal buttonText5 As String, ByVal buttonText6 As String, ByVal callbackQuery1 As String, ByVal callbackQuery2 As String, ByVal callbackQuery3 As String, ByVal callbackQuery4 As String, ByVal callbackQuery5 As String, ByVal callbackQuery6 As String, xxTelegramBotFunzione As String, xxTelegramBotParametri As String) As Object
            Dim paramString = "chat_id={0}&text={1}&reply_markup={2}"
            paramString = String.Format(paramString, chatId, text, "{""inline_keyboard"": [[{""text"":""" & buttonText1 & """,""callback_data"":""" & callbackQuery1 & """},{""text"":""" & buttonText2 & """,""callback_data"":""" & callbackQuery2 & """}],[{""text"":""" & buttonText3 & """,""callback_data"":""" & callbackQuery3 & """},{""text"":""" & buttonText4 & """,""callback_data"":""" & callbackQuery4 & """}],[{""text"":""" & buttonText5 & """,""callback_data"":""" & callbackQuery5 & """},{""text"":""" & buttonText6 & """,""callback_data"":""" & callbackQuery6 & """}]]}")
            ProattivoAggiungiAgenda = SendMessage2(paramString, chatId, "", xxTelegramBotFunzione, xxTelegramBotParametri, False)
        End Function

        Private Function ProattivoAggiungiAgendaSceltaCliente(ByVal chatId As String, ByVal text As String, ByVal buttonText As String, xxTelegramBotFunzione As String, xxTelegramBotParametri As String) As Object
            Dim paramString = "chat_id={0}&text={1}&reply_markup={2}"
            paramString = String.Format(paramString, chatId, text, "{""inline_keyboard"":[" & buttonText & "]}")
            ProattivoAggiungiAgendaSceltaCliente = SendMessage2(paramString, chatId, "", xxTelegramBotFunzione, xxTelegramBotParametri, False)
        End Function

        Private Function SendMessage2(ByVal paramString As String, xxChat_ID As String, Chiave As String, xxTelegramBotFunzione As String, xxTelegramBotParametri As String, InsertTelegramBotSQL As Boolean) As Object
            Try

                Dim apiToken As String = ConfigurationManager.AppSettings("TelegramToken").ToString()
                Dim urlString = "https://api.telegram.org/bot" & apiToken & "/sendMessage?" & paramString
                Dim request = WebRequest.Create(urlString)
                Dim rs As Stream = request.GetResponse().GetResponseStream()
                Dim reader As StreamReader = New StreamReader(rs)
                Dim ReaderString As String = reader.ReadToEnd()

                Dim jsonResulttodict = JsonConvert.DeserializeObject(Of Dictionary(Of String, Object))(ReaderString)
                Dim xxMessage_id = jsonResulttodict.Item("result")("message_id").ToString
                QueueBotMessageDeletion(xxChat_ID, xxMessage_id.ToString())
                If InsertTelegramBotSQL = True Then
                    SendMessage2 = TelegramBotSQL2(xxChat_ID, xxMessage_id.ToString, Date.Now().ToString("yyyy-MM-dd HH:mm:ss").ToString, "S", Chiave, xxTelegramBotFunzione, xxTelegramBotParametri)
                Else
                    Dim xxobj As Object
                    xxobj = xxMessage_id
                    SendMessage2 = xxobj
                End If
            Catch ex As Exception

                Exit Function
            End Try
        End Function

        'Public Async Sub scaricaAudio(voiceid As String, chatid As Long)
        Private Async Sub scaricaAudio(parm As Object)
            Dim msg = $"ERRORE nella ricezione del file audio"

            Dim chatID As Long = parm(1)
            Dim voiceID As String = parm(0)
            Dim Soprannome As String = parm(2)
            Dim SoggettiCodice As String = parm(3)
            Dim Azienda As String = parm(4)
            Dim Stream = New MemoryStream()
            Dim xxCon2 As String = ""
            Dim cn As OleDbConnection
            Dim cmd As OleDbCommand
            Dim rs As OleDbDataReader

            Select Case Azienda
                Case "BestTool"
                    cn = New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
                    xxCon2 = ConfigurationManager.AppSettings("conn2BT").ToString()
                Case "BestToolService"
                    cn = New OleDbConnection(ConfigurationManager.AppSettings("connBTS").ToString())
                    xxcon2 = ConfigurationManager.AppSettings("conn2BTS").ToString()
            End Select

            Try
                Dim xxNow As DateTime
                xxNow = Date.Now

                If botClient Is Nothing Then
                    botClient = New TelegramBotClient(ConfigurationManager.AppSettings("TelegramToken").ToString())
                End If

                Await botClient.GetInfoAndDownloadFileAsync(voiceID, Stream)
                Stream.Position = 0
                Dim bytes() As Byte
                Dim memoryStream = New MemoryStream()
                With memoryStream
                    Stream.CopyTo(memoryStream)
                    bytes = memoryStream.ToArray()
                End With
                Dim base64 As String = Convert.ToBase64String(bytes)
                Stream.Close()
                memoryStream.Close()
                Dim xxDaDataSQL = DateTime.Now.ToString("yyyy-MM-dd")

                'xxNow = CDate(FormatDateTime(Now, DateFormat.GeneralDate))

                Dim xxRapportiCodice As Integer = 0
                Dim sql As String = ""
                xxRapportiCodice = 0
                cn.Open()
                sql = "select MAX(RapportiCodice) AS Ultima from Rapporti"
                cmd = New OleDbCommand(sql, cn)
                rs = cmd.ExecuteReader
                If rs.Read Then
                    'If IsDBNull(rs.Item("Ultima")) = False Then
                    xxRapportiCodice = CInt(rs.Item("Ultima"))
                    'End If
                End If
                xxRapportiCodice = xxRapportiCodice + 1
                cn.Close()
                sql = "INSERT INTO Rapporti  (RapportiCodice, RapportiStato, RapportiCodiceCliente, RapportiCodiceClienteSede, RapportiCodiceSoggetto, RapportiData, RapportiOreEseguite, RapportiTitolo, RapportiDescrizioneAttivita, RapportiAlertSN, RapportiDUM, RapportiUDUM, RapportiOreFatturate, RapportiDescrizioneAttivitaInFattura, RapportiFattureRID, RapportiFattureRIDRiga, RapportiOra1Da, RapportiOra1A, RapportiOra2Da, RapportiOra2A, RapportiFatturareSN, RapportiDefaultFatturazione, RapportiValutazione) " &
                           " VALUES(" & xxRapportiCodice.ToString & ", 'M' , '99999' ,  '99999' , '" & SoggettiCodice & "' , '" & xxDaDataSQL & "' , '0' , '' , 'Audio delle " & DateTime.Now.ToString("HH:mm") & "' , '' , '" & DateTime.Now.ToString("yyyyMMdd HH:mm:ss") & "', '" & chatID & "', '0', '', '0', '0', '', '', '', '', '', '', '0' ) "
                Try
                    cn.Open()
                    cmd = New OleDbCommand(sql, cn)
                    cmd.ExecuteNonQuery()
                    cn.Close()
                Catch ex As Exception
                    'APPPutRapportiFromTelegram = "Errore connessione tabella rapporti inserimento rapporto dal dispositivo id " & ID.ToString
                    msg = $"ERRORE nella ricezione del file audio"
                    Exit Sub
                End Try
                cn.Close()

                'Dim bytes As Byte() = Convert.FromBase64String(Content)
                Dim commandText As String = "INSERT INTO RapportiAudio (RapportiAudioStato, RapportiAudioCodice, RapportiAudioStream, RapportiAudioDUM, RapportiAudioUDUM) VALUES('', " + xxRapportiCodice.ToString + ", @Audio, '" + DateTime.Now.ToString("yyyyMMdd HH:mm:ss") + "', '" + chatID.ToString + "') "
                Using connection As SqlConnection = New SqlConnection(xxCon2)
                    Dim command As SqlCommand = New SqlCommand(commandText, connection)
                    command.Parameters.Add("@Audio", SqlDbType.Binary)
                    command.Parameters("@Audio").Value = bytes
                    'command.Parameters.AddWithValue("@demographics", demoXml)
                    Try
                        connection.Open()
                        Dim rowsAffected As Int32 = command.ExecuteNonQuery()
                        Console.WriteLine("RowsAffected: {0}", rowsAffected)
                    Catch ex As Exception
                        Console.WriteLine(ex.Message)
                    End Try
                End Using

                'Dim rec = "|M|99999|99999||" & xxDaDataSQL & "|0||Audio delle " & String.Format(Date.Now(), "hh:mm").ToString & "||||"

            Catch ex As Exception
                msg = $"ERRORE nella ricezione del file audio"
            End Try
            msg = $"Grazie " & Soprannome & "!!! Ho inserito il tuo audio nei tuoi rapportini di oggi"

            'msg = $"Audio ricevuto!"
            SendTextMessage(chatID.ToString, msg, "", "", "")

        End Sub

        Private Function TelegramBotSQL2(xxChatID As String, xxMessageID As String, xxDateTime As String, xxSR As String, xxMessaggio As String, xxTelegramBotFunzione As String, xxTelegramBotParametri As String) As Object
            Dim cn As OleDbConnection
            Dim cmd As OleDbCommand
            Dim rs As OleDbDataReader
            Dim sql As String = ""
            Dim xxID As Object
            cn = New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
            cn.Close()
            sql = "INSERT INTO TelegramBot  (TelegramBotChatId, TelegramBotMessageId, TelegramBotMessageLocalTime, TelegramBotMessageSR, TelegramBotMessage, TelegramBotFunzione, TelegramBotParametri) " &
                       " VALUES('" & xxChatID & "', '" & xxMessageID & "', '" & xxDateTime & "', '" & xxSR & "', '" & xxMessaggio & "', '" & xxTelegramBotFunzione & "', '" & xxTelegramBotParametri & "') ; select @@Identity as Chiave "
            '.Date.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss")
            Try
                cn.Open()
                cmd = New OleDbCommand(sql, cn)
                xxID = cmd.ExecuteScalar()
                cn.Close()
                TelegramBotSQL2 = xxID
            Catch ex As Exception
                'ScriviLogFile(DateTime.Now.ToString("yyyyMMdd HH:mm:ss") & " - " & ex.Message)
                Exit Function
            End Try
            cn.Close()
        End Function

        Private Function TelegramBotSQL(xxChatID As String, xxMessageID As String, xxDateTime As String, xxSR As String, xxMessaggio As String, xxFunzione As String, xxParametri As String) As Object
            Dim cn As OleDbConnection
            Dim cmd As OleDbCommand
            Dim rs As OleDbDataReader
            Dim sql As String = ""
            Dim xxID As Object
            cn = New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
            cn.Close()
            sql = "INSERT INTO TelegramBot  (TelegramBotChatId, TelegramBotMessageId, TelegramBotMessageLocalTime, TelegramBotMessageSR, TelegramBotMessage, TelegramBotFunzione, TelegramBotParametri) " &
                       " VALUES('" & xxChatID & "', '" & xxMessageID & "', '" & xxDateTime & "', '" & xxSR & "', '" & xxMessaggio & "', '" & xxFunzione & "', '" & xxParametri & "'); select @@Identity as Chiave"
            '.Date.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss")
            Try
                cn.Open()
                cmd = New OleDbCommand(sql, cn)
                xxID = cmd.ExecuteScalar()
                cn.Close()
            Catch ex As Exception

                Exit Function
            End Try
            TelegramBotSQL = xxID
            cn.Close()
        End Function

        Private Sub GestisciMessaggio(ByVal update As Update)

            Dim AUT00 As New DataTable("AUT00")
            'AUT00 = CreaDataTableAutorizzazioniTelegram()
            AUT00 = VerificaAutorizzazioni(update.Message.Chat.Id.ToString)
            Dim xxChiave As Object

            If update.Message.Type = Telegram.Bot.Types.Enums.MessageType.Text Then
                '  -> SOLO PER MUCCIO If update.Message.Text <> "/start" Then xxChiave = TelegramBotSQL(update.Message.Chat.Id.ToString, update.Message.MessageId.ToString, update.Message.Date.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss").ToString, "R", update.Message.Text.Replace("""", "").Replace("'", "''"), "", "")
                If update.Message.Text <> "/start" Then xxChiave = TelegramBotSQL(update.Message.Chat.Id.ToString, update.Message.MessageId.ToString, update.Message.Date.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss").ToString, "R", update.Message.Text.Replace("""", "").Replace("'", "''"), "", "")
            Else
                xxChiave = TelegramBotSQL(update.Message.Chat.Id.ToString, update.Message.MessageId.ToString, update.Message.Date.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss").ToString, "R", "#Audio", "", "")
            End If
            QueueBotMessageDeletion(update.Message.Chat.Id.ToString(), update.Message.MessageId.ToString())

            If AUT00.Rows.Count = 0 Then
                Dim query = "Registra||" & update.Message.Chat.Id.ToString() & "||" & update.Message.From.FirstName & "||" & update.Message.From.LastName
                Dim msg = "Utente non registrato. Cliccare su ""Invia"" per inviare una richiesta di registrazione."
                SendCallbackButtonMessage(update.Message.Chat.Id.ToString(), msg, "Invia", query)
                Exit Sub
            End If

            Dim xxSoprannome As String = ""
            Dim xxSoggettiCodice As String = ""
            Dim xxEmail As String = ""
            Dim xxAzienda As String = ""
            Try
                xxSoprannome = AUT00.Rows(0).Item("SoggettiSoprannome").ToString
                xxSoggettiCodice = AUT00.Rows(0).Item("SoggettiCodice").ToString
                xxAzienda = AUT00.Rows(0).Item("Azienda").ToString
                xxEmail = AUT00.Rows(0).Item("SoggettiEmail").ToString
            Catch ex As Exception

            End Try

            If (update.Message.Type = Telegram.Bot.Types.Enums.MessageType.Voice) Then
                If AUT00.Rows(0).Item("DeviceFunzione1").ToString = "S" Then
                    Dim InstanceCaller As New Thread(New ParameterizedThreadStart(AddressOf scaricaAudio))
                    Dim param As Object = New Object() {update.Message.Voice.FileId, update.Message.Chat.Id, xxSoprannome, xxSoggettiCodice, xxAzienda}
                    InstanceCaller.Start(param)
                End If
            End If

            If (update.Message.Type = Telegram.Bot.Types.Enums.MessageType.Text) Then
                Dim text = update.Message.Text
                If Not Equals(text, String.Empty) Then
                    If String.Equals(text.Trim(), "/annulla", StringComparison.OrdinalIgnoreCase) OrElse String.Equals(text.Trim(), "annulla", StringComparison.OrdinalIgnoreCase) Then
                        If Not CancelActiveTimesheetDraft(update.Message.Chat.Id.ToString()) Then
                            SendTextMessage(update.Message.Chat.Id.ToString(), "Non ci sono rapportini in corso da annullare.", "", "", "")
                        End If
                        Exit Sub
                    End If

                    If Not String.Equals(text, "/start", StringComparison.OrdinalIgnoreCase) AndAlso HandleTimesheetText(update.Message.Chat.Id.ToString(), text.Trim()) Then
                        Exit Sub
                    End If

                    Dim msgHelp As String = ""
                    msgHelp = "BT-Bot rapportini" & Environment.NewLine
                    msgHelp += "? Help" & Environment.NewLine
                    msgHelp += "R oppure /rapportino Inserisci rapportino " & Environment.NewLine
                    msgHelp += "C Cancella messaggi del bot " & Environment.NewLine
                    msgHelp += "/annulla Annulla inserimento rapportino" & Environment.NewLine

                    If text = "/start" Then
                        Dim msgWellcome As String = ""
                        msgWellcome = "BT-Bot rapportini al tuo servizio" & Environment.NewLine & "( ? per aiuto )"
                        Dim paramString = "chat_id={0}&text={1}"
                        paramString = String.Format(paramString, update.Message.Chat.Id.ToString, msgWellcome)
                        SendMessage2(paramString, update.Message.Chat.Id.ToString, "", "", "", False)
                        Exit Sub
                    End If

                    If String.Equals(text.Trim(), "/rapportino", StringComparison.OrdinalIgnoreCase) OrElse
                       String.Equals(text.Trim(), "/rapportini", StringComparison.OrdinalIgnoreCase) OrElse
                       String.Equals(text.Trim(), "rapportino", StringComparison.OrdinalIgnoreCase) OrElse
                       String.Equals(text.Trim(), "rapportini", StringComparison.OrdinalIgnoreCase) Then
                        StartTimesheetDraft(update.Message.Chat.Id.ToString)
                        Exit Sub
                    End If

                    If text.Length = 1 Then
                        Select Case text.ToUpper.Substring(0, 1)
                            Case "?"
                                Me.SendTextMessage(update.Message.Chat.Id.ToString, msgHelp, "", "", "")
                            Case "R"
                                StartTimesheetDraft(update.Message.Chat.Id.ToString)
                            Case "C"
                                AskClearChatConfirmation(update.Message.Chat.Id.ToString())
                            Case Else
                                Me.SendTextMessage(update.Message.Chat.Id.ToString, msgHelp, "", "", "")
                        End Select
                    Else
                        Me.SendTextMessage(update.Message.Chat.Id.ToString, msgHelp, "", "", "")
                    End If
                End If
            End If
        End Sub

        Public Sub InviaMessaggioATuttiIDevice(msgText As String, SoloMatteoPiero As Boolean)
            Dim cn As OleDbConnection
            Dim cmd As OleDbCommand
            Dim rs As OleDbDataReader
            Dim sql As String = ""
            cn = New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
            sql = "select 'BestTool' as Azienda, DeviceID, DeviceUtenteID,  " &
                                " SoggettiNome,  " &
                                " SoggettiCognome, " &
                                " iif(isnull(SoggettiSoprannome, '') = '', SoggettiNome, SoggettiSoprannome) as SoggettiSoprannome, " &
                                " CONCAT(SoggettiNome, ' ', SoggettiCognome) as SoggettiNomeCognome,  " &
                                " SoggettiCodice, " &
                                " SoggettiCodice,  " &
                                " SoggettiEmail  " &
                                " from Device inner join Utenti on UtentiID = DeviceUtenteID inner join Soggetti on SoggettiCodice = UtentiCodiceSoggetto " &
                                " where SoggettiStato <> 'A' and UtentiStato <> 'A' and DeviceStato <> 'A' and isnull(DeviceTelegram, '') = 'S' "
            If SoloMatteoPiero = True Then
                sql += " and deviceid = '723106604' or deviceid = '731173539'"
            End If
            '" where SoggettiStato <> 'A' and UtentiStato <> 'A' and DeviceStato <> 'A' and DeviceId = '723106604'"
            cn.Open()
            cmd = New OleDbCommand(sql, cn)
            rs = cmd.ExecuteReader
            msgText = " * " & msgText & " * "
            Do While rs.Read
                If rs.Item("DeviceID").ToString <> "" Then
                    Try
                        SendTextMessage(rs.Item("DeviceID").ToString, msgText.Replace("<soggetto>", rs.Item("SoggettiSoprannome").ToString), "", "", "")
                    Catch ex As Exception
                    End Try
                End If
            Loop
            cn.Close()

            cn = New OleDbConnection(ConfigurationManager.AppSettings("connBTS").ToString())
            sql = "select 'BestTool' as Azienda, DeviceID, DeviceUtenteID,  " &
                                " SoggettiNome,  " &
                                " SoggettiCognome, " &
                                " iif(isnull(SoggettiSoprannome, '') = '', SoggettiNome, SoggettiSoprannome) as SoggettiSoprannome, " &
                                " CONCAT(SoggettiNome, ' ', SoggettiCognome) as SoggettiNomeCognome,  " &
                                " SoggettiCodice, " &
                                " SoggettiCodice,  " &
                                " SoggettiEmail  " &
                                " from Device inner join Utenti on UtentiID = DeviceUtenteID inner join Soggetti on SoggettiCodice = UtentiCodiceSoggetto " &
                                " where SoggettiStato <> 'A' and UtentiStato <> 'A' and DeviceStato <> 'A' and isnull(DeviceTelegram, '') = 'S' "
            If SoloMatteoPiero = True Then
                sql += " and deviceid = '723106604' or deviceid = '731173539'"
            End If
            '" where SoggettiStato <> 'A' and UtentiStato <> 'A' and DeviceStato <> 'A' and DeviceId = '723106604'"
            cn.Open()
            cmd = New OleDbCommand(sql, cn)
            rs = cmd.ExecuteReader
            msgText = " * " & msgText & " * "
            Do While rs.Read
                If rs.Item("DeviceID").ToString <> "" Then
                    Try
                        SendTextMessage(rs.Item("DeviceID").ToString, msgText.Replace("<soggetto>", rs.Item("SoggettiSoprannome").ToString), "", "", "")
                    Catch ex As Exception
                    End Try
                End If
            Loop
            cn.Close()

        End Sub

        Public Sub InviaElencoClienti(ByVal chatId As String, msgText As String, DalNumero As Integer)
            Dim cn As OleDbConnection
            Dim cmd As OleDbCommand
            Dim rs As OleDbDataReader
            Dim sql As String = ""
            Dim Pulsanti As String = ""
            cn = New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
            sql = "SELECT TOP 10 * " &
                    " From clienti " &
                    " WHERE ClientiRagioneSociale LIKE '%" & msgText & "%' and clientistato <> 'A' " &
                    " Order BY  " &
                    " CHARINDEX('" & msgText & "', ClientiRagioneSociale, 1), ClientiRagioneSociale  "
            cn.Open()
            cmd = New OleDbCommand(sql, cn)
            rs = cmd.ExecuteReader
            msgText = " * " & msgText & " * "
            Do While rs.Read
                Try
                    'Pulsanti += "[{""text"":""" & rs.Item("ClientiRagioneSociale").ToString & """,""callback_data"":""" & "SetCalendar2||" & xxChiave.ToString & "||99||" & rs.Item("ClientiSediCodice").ToString & "|||" & """}],"
                    Pulsanti += rs.Item("ClientiRagioneSociale").ToString & Environment.NewLine
                Catch ex As Exception

                End Try
            Loop
            cn.Close()

            'SendTextMessage(rs.Item("DeviceID").ToString, msgText.Replace("<soggetto>", rs.Item("SoggettiSoprannome").ToString), "", "", "")         
            SendTextMessage(chatId, Pulsanti, "", "", "")
        End Sub
        Public Sub InfoReperibilità(ChatId As String)
            Dim cn As OleDbConnection
            cn = New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
            Dim cmd As OleDbCommand
            Dim rs As OleDbDataReader
            Dim sql As String = ""
            Dim msgText As String = ""
            sql = " Declare @AppData date  " &
                  " Set @AppData = '" & DateTime.Now.ToString("yyyyMMdd") & "' " &
                          " Select " &
                         " (Select iif(CalendarioTipo = 'L', 'Lavorativo', 'Non Lavorativo') from calendario where calendariodata = @AppData) as TipoGiorno " &
                         " ,iif(DATEPART(dw,@AppData) = 1,R1.ReperibilitaDescrizione,iif(DATEPART(dw,@AppData) = 2,R2.ReperibilitaDescrizione,iif(DATEPART(dw,@AppData) = 3,R3.ReperibilitaDescrizione,iif(DATEPART(dw,@AppData) = 4,R4.ReperibilitaDescrizione,iif(DATEPART(dw,@AppData) = 5,R5.ReperibilitaDescrizione,iif(DATEPART(dw,@AppData) = 6,R6.ReperibilitaDescrizione,iif(DATEPART(dw,@AppData) = 7,R7.ReperibilitaDescrizione,''))))))) as Descrizione  " &
                         " ,iif(DATEPART(dw,@AppData) = 1,R1.ReperibilitaNumeroOre,iif(DATEPART(dw,@AppData) = 2,R2.ReperibilitaNumeroOre,iif(DATEPART(dw,@AppData) = 3,R3.ReperibilitaNumeroOre,iif(DATEPART(dw,@AppData) = 4,R4.ReperibilitaNumeroOre,iif(DATEPART(dw,@AppData) = 5,R5.ReperibilitaNumeroOre,iif(DATEPART(dw,@AppData) = 6,R6.ReperibilitaNumeroOre,iif(DATEPART(dw,@AppData) = 7,R7.ReperibilitaNumeroOre,''))))))) as ReperibilitaNumeroOre  " &
                         " ,iif(DATEPART(dw,@AppData) = 1,R1.ReperibilitaCodice,iif(DATEPART(dw,@AppData) = 2,R2.ReperibilitaCodice,iif(DATEPART(dw,@AppData) = 3,R3.ReperibilitaCodice,iif(DATEPART(dw,@AppData) = 4,R4.ReperibilitaCodice,iif(DATEPART(dw,@AppData) = 5,R5.ReperibilitaCodice,iif(DATEPART(dw,@AppData) = 6,R6.ReperibilitaCodice,iif(DATEPART(dw,@AppData) = 7,R7.ReperibilitaCodice,''))))))) as xxReperibilitaCodice" &
                         " , ClientiRagioneSociale, CommesseDescrizione, ClientiCodice, CommesseCodice  " &
                         " ,concat(substring(Soggettinome,1,1), '.', soggetticognome) as SoggettiDescrizione" &
                         " , SoggettiReperibilitaEmail" &
                         " , SoggettiReperibilitaNumero" &
                         " From commesse   " &
                         "  inner join clienti on clienticodice = CommesseCodiceCliente   " &
                         " left join CommesseReperibilita on CommesseCodice = CommesseReperibilitaCodiceCommessa  " &
                         " left join Reperibilita as R1 on R1.ReperibilitaCodice = CommesseReperibilitaCodiceReperibilita1  " &
                         " left join Reperibilita as R2 on R2.ReperibilitaCodice = CommesseReperibilitaCodiceReperibilita2  " &
                         "  left join Reperibilita as R3 on R3.ReperibilitaCodice = CommesseReperibilitaCodiceReperibilita3  " &
                         " left join Reperibilita as R4 on R4.ReperibilitaCodice = CommesseReperibilitaCodiceReperibilita4  " &
                         "  left join Reperibilita as R5 on R5.ReperibilitaCodice = CommesseReperibilitaCodiceReperibilita5  " &
                         " left join Reperibilita as R6 on R6.ReperibilitaCodice = CommesseReperibilitaCodiceReperibilita6  " &
                         "  left join Reperibilita as R7 on R7.ReperibilitaCodice = CommesseReperibilitaCodiceReperibilita7  	" &
                    " inner join Rapporti on RapportiCodiceCommessa = CommesseCodice and RapportiData = '" & DateTime.Now.ToString("yyyyMMdd") & "' " &
                    "  left join Soggetti on SoggettiCodice = RapportiCodiceSoggetto " &
                    "  where ClientiCodice <> 77 and CommesseTipo = 'R' and CommesseStato <> 'A' and (CommesseValidaDa <= '" & DateTime.Now.ToString("yyyyMMdd") & "') AND (CommesseValidaA >= '" & DateTime.Now.ToString("yyyyMMdd") & "') "
            cn.Open()
            cmd = New OleDbCommand(sql, cn)
            rs = cmd.ExecuteReader
            msgText = ""
            Do While rs.Read
                If rs.Item("xxReperibilitaCodice").ToString <> "0" Then
                    If msgText = "" Then msgText = " * Reperibilità di " & DateTime.Now.ToString("dddd, dd MMMM yyyy") & " * " & Environment.NewLine
                    msgText += "________________" & Environment.NewLine
                    msgText += rs.Item("ClientiRagioneSociale").ToString & Environment.NewLine
                    msgText += rs.Item("Descrizione").ToString & Environment.NewLine
                    msgText += rs.Item("SoggettiDescrizione").ToString & Environment.NewLine
                    msgText += rs.Item("SoggettiReperibilitaEmail").ToString & Environment.NewLine
                    msgText += rs.Item("SoggettiReperibilitaNumero").ToString & Environment.NewLine
                    msgText += "________________" & Environment.NewLine
                End If
            Loop
            If msgText = "" Then msgText = "* NESSUN SERVIZIO DI REPERIBILITà NELLA GIORNATA DI OGGI *"
            SendTextMessage(ChatId, msgText, "", "", "")
        End Sub

        Private Sub GetCalendar(ByVal soggetto As String, ByVal data As String, ByVal chatId As String)
            Dim service = New SyncMobile.SyncMobileSoapClient()
            service.Endpoint.Binding.SendTimeout = New TimeSpan(2, 0, 0)
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12
            Dim rec = $"|tipo=calendar|persona={soggetto}|campi=Subject,calendardatetime,starttime,endtime|mydata=" & data.Replace("-", "")
            Dim res = String.Empty

            Try
                res = service.GetDominoDataQuery(chatId, String.Empty, String.Empty, String.Empty, rec)
            Catch ex As Exception
                res = "ERRORE: " & ex.Message
            End Try

            Dim [error] = False
            Dim msgText = String.Empty

            If res.StartsWith("ERRORE") Then
                msgText = res
                [error] = True
            Else

                Try
                    Dim obj = JsonConvert.DeserializeObject(Of List(Of JsonCalendar))(res)
                    Dim soggTemp = String.Empty

                    For Each o In obj

                        If Not Equals(soggTemp, o.FULLNAME) Then
                            If Not Equals(msgText, String.Empty) Then msgText += Microsoft.VisualBasic.Constants.vbLf & Microsoft.VisualBasic.Constants.vbLf
                            msgText += o.FULLNAME & Microsoft.VisualBasic.Constants.vbLf
                            msgText += "(" & Convert.ToDateTime(data).ToLongDateString() & ")" & Microsoft.VisualBasic.Constants.vbLf
                            soggTemp = o.FULLNAME
                        End If

                        If Equals(o.SUBJECT.Trim(), "Nessun appuntamento oggi") Then
                            msgText += Microsoft.VisualBasic.Constants.vbLf & o.SUBJECT.Trim()
                        Else
                            msgText += Microsoft.VisualBasic.Constants.vbLf & Convert.ToDateTime(o.STARTTIME).ToString("HH:mm") & " - " & Convert.ToDateTime(o.ENDTIME).ToString("HH:mm") & " " & o.SUBJECT.Trim().Replace("#", "")
                        End If
                    Next

                Catch __unusedException1__ As Exception
                End Try
            End If

            If Equals(msgText, String.Empty) OrElse [error] = True Then
                If Equals(msgText, String.Empty) Then
                    msgText = "Nessun soggetto trovato"
                End If
                'var msg = botClient.SendTextMessageAsync(
                '    chatId: chatId,
                '    text: msgText,
                '    parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                '    disableNotification: true
                ').Result;
                SendTextMessage(chatId, msgText, "", "", "")
            Else
                Dim query = "GetCalendar||" & soggetto & "||" & Convert.ToDateTime(data).AddDays(1).ToString("yyyy-MM-dd") & "||" & chatId
                'var msg = botClient.SendTextMessageAsync(
                '    chatId: chatId,
                '    text: msgText,
                '    parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                '    disableNotification: true,
                '    replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("Next", query))
                ').Result;
                SendCallbackButtonMessage(chatId, msgText, "Next", query)
            End If
        End Sub

        Private Sub SetCalendar(ByVal TestoAppuntamento As String, ByVal dataAppuntamento As String, ByVal chatId As String, Soggetto As String, xxMessage As String, xxFunzione As String, xxParametri As String)
            Dim service = New SyncMobile.SyncMobileSoapClient()
            service.Endpoint.Binding.SendTimeout = New TimeSpan(2, 0, 0)
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12
            Dim rec = $"|tipo=inscalendar|email=" & Soggetto & "|subject=" & TestoAppuntamento.Replace("""", "").Replace("'", "''").Replace("|", "") & "|calendardatetime=" & dataAppuntamento
            Dim res = String.Empty

            Try
                res = service.GetDominoDataQuery(chatId, String.Empty, String.Empty, String.Empty, rec)
            Catch ex As Exception
                res = "ERRORE: " & ex.Message
            End Try

            Dim [error] = False
            Dim msgText = String.Empty

            If res.StartsWith("ERRORE") Then
                msgText = res
                [error] = True
            Else

                Try
                    Dim DataAppuntamentoDate As New Date
                    DataAppuntamentoDate = CDate(dataAppuntamento)
                    'Dim obj = JsonConvert.DeserializeObject(Of List(Of JsonCalendar))(res)
                    'Dim soggTemp = String.Empty
                    SendTextMessage(chatId, "Appuntamento inserito in agenda nel giorno " & DataAppuntamentoDate.ToString("dd/MM/yyyy").ToString & "!! ", xxMessage, xxFunzione, xxParametri)
                Catch __unusedException1__ As Exception
                End Try
            End If

            If Equals(msgText, String.Empty) OrElse [error] = True Then

            Else
                Dim query = "GetCalendar||" & "A" & "||" & Convert.ToDateTime(dataAppuntamento).AddDays(1).ToString("yyyy-MM-dd") & "||" & chatId
                'var msg = botClient.SendTextMessageAsync(
                '    chatId: chatId,
                '    text: msgText,
                '    parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                '    disableNotification: true,
                '    replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("Next", query))
                ').Result;   

                SendCallbackButtonMessage(chatId, msgText, "Next", query)
            End If
        End Sub

        Private Shared Sub InviaMail(ByVal id As String, ByVal nominativo As String)
            Dim mittente = ConfigurationManager.AppSettings("SMTPSender")
            Dim destinatario = ConfigurationManager.AppSettings("SMTPDestinatario")
            Dim smtpServer = ConfigurationManager.AppSettings("SMTPServer")
            Dim smtpUser = ConfigurationManager.AppSettings("SMTPUser")
            Dim smtpUserPass = ConfigurationManager.AppSettings("SMTPUserPass")
            Dim smtpPort = If(Equals(ConfigurationManager.AppSettings("SMTPPort"), String.Empty), 25, Convert.ToInt32(ConfigurationManager.AppSettings("SMTPPort")))
            Dim smtpSSLType = ConfigurationManager.AppSettings("SMTPSslType")

            Using msg = New MimeMailMessage()
                Dim html = "<p><b>Buongiorno,</b></p>"
                html += $"<p>l'utente {nominativo} ha richiesto l'abilitazione del suo dispositivo con id <b>{id}</b> ad accedere a btCatalog da Telegram. " & "Per validare la richiesta occorre collegarsi all'interfaccia web di <a href='https://service.besttool.it/'>btCatalog</a> " & "ed abilitare l'id sopra indicato</p>"
                html += "<p>Grazie</p>"
                msg.From = New MailAddress(mittente)
                msg.To.Add(New MailAddress(destinatario))
                msg.Subject = "btCat@log - Richiesta di abilitazione da dispositivo mobile Telegram"
                msg.Body = html
                msg.IsBodyHtml = True

                Using mailer = New MimeMailer(smtpServer, smtpPort)

                    If String.IsNullOrEmpty(smtpUser) Then
                        mailer.AuthenticationMode = AuthenticationType.UseDefualtCridentials
                    Else
                        mailer.User = smtpUser
                        mailer.Password = smtpUserPass
                    End If

                    mailer.SslType = CType([Enum].Parse(GetType(SslMode), smtpSSLType), SslMode)
                    'mailer.AuthenticationMode = AuthenticationType.Base64;
                    mailer.AuthenticationMode = AuthenticationType.UseDefualtCridentials
                    mailer.SendMail(msg)
                End Using
            End Using
        End Sub

        Private Sub Registra(ByVal chatId As String, ByVal nome As String, ByVal cognome As String)
            Dim nominativo = $"{nome} {cognome}".Trim()

            Try
                InviaMail(chatId, nominativo)

                Dim text = $"È stata inviata una richiesta di registrazione del tuo device su btCatalog (ID *{chatId}*)." & Microsoft.VisualBasic.Constants.vbLf & "Attendere una risposta da parte dell'amministrazione."
                Me.SendTextMessage(chatId, text, "", "", "")
            Catch ex As Exception
                Dim text = $"È stato riscontrato un errore durante l'invio della richiesta di registrazione del tuo device su btCatalog: {ex.Message}" & Microsoft.VisualBasic.Constants.vbLf & "Contattare l'amministratore."
                Me.SendTextMessage(chatId, text, "", "", "")
            End Try
        End Sub

        Private Sub SendCallbackButtonMessage(ByVal chatId As String, ByVal text As String, ByVal buttonText As String, ByVal callbackQuery As String)
            Dim paramString = "chat_id={0}&text={1}&reply_markup={2}"
            paramString = String.Format(paramString, chatId, text, "{""inline_keyboard"":[[{""text"":""" & buttonText & """,""callback_data"":""" & callbackQuery & """}]]}")
            SendMessage(paramString, chatId, "", "", "")
        End Sub
        Private Sub SelectDateCalendarOggiDomani(ByVal chatId As String, ByVal text As String, ByVal buttonTextOggi As String, ByVal buttonTextDomani As String, ByVal buttonTextDopoDomani As String, ByVal buttonTextDopoDopoDomani As String, ByVal callbackQueryOggi As String, ByVal callbackQueryDomani As String, ByVal callbackQueryDopoDomani As String, ByVal callbackQueryDopoDopoDomani As String)
            Dim paramString = "chat_id={0}&text={1}&reply_markup={2}"
            paramString = String.Format(paramString, chatId, text, "{""inline_keyboard"":[[{""text"":""" & buttonTextOggi & """,""callback_data"":""" & callbackQueryOggi & """},{""text"":""" & buttonTextDomani & """,""callback_data"":""" & callbackQueryDomani & """},{""text"":""" & buttonTextDopoDomani & """,""callback_data"":""" & callbackQueryDopoDomani & """},{""text"":""" & buttonTextDopoDopoDomani & """,""callback_data"":""" & callbackQueryDopoDopoDomani & """}]]}")
            SendMessage(paramString, chatId, "", "", "")
        End Sub

        Private Sub SelectDateCalendar(ByVal chatId As String, Soggetto As String, xxChiave As Object)

            Dim Oggi As New Date
            Dim Domani As New Date
            Dim DopoDomani As New Date
            Dim DopoDopoDomani As New Date
            Dim cn As OleDbConnection
            Dim cmd As OleDbCommand
            Dim sql As String = ""
            cn = New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
            Dim rs As OleDbDataReader


            Oggi = Date.Now
            Domani = Date.Now.AddDays(1)
            DopoDomani = Date.Now.AddDays(2)
            DopoDopoDomani = Date.Now.AddDays(3)

            Dim Parametri = Soggetto & "||" & Oggi.ToString("yyyy-MM-dd") & "||" & Domani.ToString("yyyy-MM-dd") & "||" & DopoDomani.ToString("yyyy-MM-dd") & "||" & DopoDopoDomani.ToString("yyyy-MM-dd") & "||" & chatId

            'Aggiorno il recordset mettendomi i parametri che mi servono per il callback

            sql = "UPDATE TelegramBot Set TelegramBotFunzione = 'SetCalendar', TelegramBotParametri = '" & Parametri & "' where TelegramBotID = " & xxChiave.ToString
            Try
                cn.Open()
                cmd = New OleDbCommand(sql, cn)
                cmd.ExecuteNonQuery()
                cn.Close()
            Catch ex As Exception

                Exit Sub
            End Try
            cn.Close()

            SelectDateCalendarOggiDomani(chatId, "In quale giorno lo vuoi inserire", "Oggi", "Domani", DopoDomani.ToString("ddd dd-MM"), DopoDopoDomani.ToString("ddd dd-MM"), "SetCalendar||" & xxChiave.ToString & "||1", "SetCalendar||" & xxChiave.ToString & "||2", "SetCalendar||" & xxChiave.ToString & "||3", "SetCalendar||" & xxChiave.ToString & "||4")

        End Sub


        Private Sub SendTextMessage(ByVal chatId As String, ByVal text As String, xxMessage As String, xxFunzione As String, xxParametri As String)
            Dim paramString = "chat_id={0}&text={1}"
            paramString = String.Format(paramString, chatId, text)
            SendMessage(paramString, chatId, xxMessage, xxFunzione, xxParametri)
        End Sub

        Private Sub SendMessage(ByVal paramString As String, xxChat_ID As String, xxMessage As String, xxFunzione As String, xxParametri As String)
            Dim apiToken As String = ConfigurationManager.AppSettings("TelegramToken").ToString()
            Dim urlString = "https://api.telegram.org/bot" & apiToken & "/sendMessage?" & paramString
            Dim request = WebRequest.Create(urlString)
            Dim rs As Stream = request.GetResponse().GetResponseStream()
            Dim reader As StreamReader = New StreamReader(rs)
            Dim ReaderString As String = reader.ReadToEnd()

            Dim jsonResulttodict = JsonConvert.DeserializeObject(Of Dictionary(Of String, Object))(ReaderString)

            Dim xxMessage_id = jsonResulttodict.Item("result")("message_id").ToString
            QueueBotMessageDeletion(xxChat_ID, xxMessage_id.ToString())

            TelegramBotSQL(xxChat_ID, xxMessage_id.ToString, Date.Now().ToString("yyyy-MM-dd HH:mm:ss").ToString, "S", xxMessage, xxFunzione, xxParametri)

        End Sub

        Public Shared Function VerificaAutorizzazioni(xxDevice As String) As DataTable

            VerificaAutorizzazioni = New DataTable("AUT00")

            Dim Azienda As DataColumn = New DataColumn("Azienda")
            Azienda.DataType = System.Type.GetType("System.String")
            VerificaAutorizzazioni.Columns.Add(Azienda)

            Dim DeviceID As DataColumn = New DataColumn("DeviceID")
            DeviceID.DataType = System.Type.GetType("System.String")
            VerificaAutorizzazioni.Columns.Add(DeviceID)

            Dim DeviceUtenteID As DataColumn = New DataColumn("DeviceUtenteID")
            DeviceUtenteID.DataType = System.Type.GetType("System.String")
            VerificaAutorizzazioni.Columns.Add(DeviceUtenteID)

            Dim DeviceFunzione1 As DataColumn = New DataColumn("DeviceFunzione1")
            DeviceFunzione1.DataType = System.Type.GetType("System.String")
            VerificaAutorizzazioni.Columns.Add(DeviceFunzione1)

            Dim DeviceFunzione2 As DataColumn = New DataColumn("DeviceFunzione2")
            DeviceFunzione2.DataType = System.Type.GetType("System.String")
            VerificaAutorizzazioni.Columns.Add(DeviceFunzione2)

            Dim DeviceFunzione3 As DataColumn = New DataColumn("DeviceFunzione3")
            DeviceFunzione3.DataType = System.Type.GetType("System.String")
            VerificaAutorizzazioni.Columns.Add(DeviceFunzione3)

            Dim DeviceFunzione4 As DataColumn = New DataColumn("DeviceFunzione4")
            DeviceFunzione4.DataType = System.Type.GetType("System.String")
            VerificaAutorizzazioni.Columns.Add(DeviceFunzione4)

            Dim DeviceFunzione5 As DataColumn = New DataColumn("DeviceFunzione5")
            DeviceFunzione5.DataType = System.Type.GetType("System.String")
            VerificaAutorizzazioni.Columns.Add(DeviceFunzione5)

            Dim DeviceFunzione6 As DataColumn = New DataColumn("DeviceFunzione6")
            DeviceFunzione6.DataType = System.Type.GetType("System.String")
            VerificaAutorizzazioni.Columns.Add(DeviceFunzione6)

            Dim DeviceFunzione7 As DataColumn = New DataColumn("DeviceFunzione7")
            DeviceFunzione7.DataType = System.Type.GetType("System.String")
            VerificaAutorizzazioni.Columns.Add(DeviceFunzione7)

            Dim DeviceFunzione8 As DataColumn = New DataColumn("DeviceFunzione8")
            DeviceFunzione8.DataType = System.Type.GetType("System.String")
            VerificaAutorizzazioni.Columns.Add(DeviceFunzione8)

            Dim DeviceFunzione9 As DataColumn = New DataColumn("DeviceFunzione9")
            DeviceFunzione9.DataType = System.Type.GetType("System.String")
            VerificaAutorizzazioni.Columns.Add(DeviceFunzione9)

            Dim DeviceFunzione10 As DataColumn = New DataColumn("DeviceFunzione10")
            DeviceFunzione10.DataType = System.Type.GetType("System.String")
            VerificaAutorizzazioni.Columns.Add(DeviceFunzione10)

            Dim DeviceFunzione11 As DataColumn = New DataColumn("DeviceFunzione11")
            DeviceFunzione11.DataType = System.Type.GetType("System.String")
            VerificaAutorizzazioni.Columns.Add(DeviceFunzione11)

            Dim SoggettiNome As DataColumn = New DataColumn("SoggettiNome")
            SoggettiNome.DataType = System.Type.GetType("System.String")
            VerificaAutorizzazioni.Columns.Add(SoggettiNome)

            Dim SoggettiCognome As DataColumn = New DataColumn("SoggettiCognome")
            SoggettiCognome.DataType = System.Type.GetType("System.String")
            VerificaAutorizzazioni.Columns.Add(SoggettiCognome)

            Dim SoggettiSoprannome As DataColumn = New DataColumn("SoggettiSoprannome")
            SoggettiSoprannome.DataType = System.Type.GetType("System.String")
            VerificaAutorizzazioni.Columns.Add(SoggettiSoprannome)

            Dim SoggettiNomeCognome As DataColumn = New DataColumn("SoggettiNomeCognome")
            SoggettiNomeCognome.DataType = System.Type.GetType("System.String")
            VerificaAutorizzazioni.Columns.Add(SoggettiNomeCognome)

            Dim SoggettiCodice As DataColumn = New DataColumn("SoggettiCodice")
            SoggettiCodice.DataType = System.Type.GetType("System.String")
            VerificaAutorizzazioni.Columns.Add(SoggettiCodice)

            Dim SoggettiEmail As DataColumn = New DataColumn("SoggettiEmail")
            SoggettiEmail.DataType = System.Type.GetType("System.String")
            VerificaAutorizzazioni.Columns.Add(SoggettiEmail)


            Dim cn As OleDbConnection
            Dim cmd As OleDbCommand
            Dim rs As OleDbDataReader
            Dim sql As String = ""
            Dim row As DataRow
            cn = New OleDbConnection(ConfigurationManager.AppSettings("connBT").ToString())
            cn.Open()
            sql = "select 'BestTool' as Azienda, DeviceID, DeviceUtenteID,  " &
                                " DeviceFunzione1, " &
                                " DeviceFunzione2,  " &
                                " DeviceFunzione3, " &
                                " DeviceFunzione4,  " &
                                " DeviceFunzione5, " &
                                " DeviceFunzione6,  " &
                                " DeviceFunzione7, " &
                                " DeviceFunzione8,  " &
                                " DeviceFunzione9, " &
                                " DeviceFunzione10,  " &
                                " DeviceFunzione11, " &
                                " SoggettiNome,  " &
                                " SoggettiCognome, " &
                                " isnull(SoggettiSoprannome, '') as SoggettiSoprannome, " &
                                " CONCAT(SoggettiNome, ' ', SoggettiCognome) as SoggettiNomeCognome,  " &
                                " SoggettiCodice, " &
                                " SoggettiCodice,  " &
                                " SoggettiEmail  " &
                                " from Device inner join Utenti on UtentiID = DeviceUtenteID inner join Soggetti on SoggettiCodice = UtentiCodiceSoggetto " &
                                " where SoggettiStato <> 'A' and UtentiStato <> 'A' and DeviceStato <> 'A' and DeviceID = '" & xxDevice & "'"
            cmd = New OleDbCommand(sql, cn)
            rs = cmd.ExecuteReader
            If rs.Read Then
                row = VerificaAutorizzazioni.NewRow()
                row.Item("DeviceID") = rs.Item("DeviceID")
                row.Item("Azienda") = rs.Item("Azienda")
                row.Item("DeviceUtenteID") = rs.Item("DeviceUtenteID")
                row.Item("DeviceFunzione1") = rs.Item("DeviceFunzione1")
                row.Item("DeviceFunzione2") = rs.Item("DeviceFunzione2")
                row.Item("DeviceFunzione3") = rs.Item("DeviceFunzione3")
                row.Item("DeviceFunzione4") = rs.Item("DeviceFunzione4")
                row.Item("DeviceFunzione5") = rs.Item("DeviceFunzione5")
                row.Item("DeviceFunzione6") = rs.Item("DeviceFunzione6")
                row.Item("DeviceFunzione7") = rs.Item("DeviceFunzione7")
                row.Item("DeviceFunzione8") = rs.Item("DeviceFunzione8")
                row.Item("DeviceFunzione9") = rs.Item("DeviceFunzione9")
                row.Item("DeviceFunzione10") = rs.Item("DeviceFunzione10")
                row.Item("DeviceFunzione11") = rs.Item("DeviceFunzione11")
                row.Item("SoggettiNome") = rs.Item("SoggettiNome")
                row.Item("SoggettiCognome") = rs.Item("SoggettiCognome")
                If rs.Item("SoggettiSoprannome").ToString <> "" Then
                    row.Item("SoggettiSoprannome") = rs.Item("SoggettiSoprannome")
                Else
                    row.Item("SoggettiSoprannome") = rs.Item("SoggettiNome")
                End If
                row.Item("SoggettiNomeCognome") = rs.Item("SoggettiNomeCognome")
                row.Item("SoggettiCodice") = rs.Item("SoggettiCodice")
                row.Item("SoggettiEmail") = rs.Item("SoggettiEmail")
                VerificaAutorizzazioni.Rows.Add(row)
            End If
            cn.Close()

            cn = New OleDbConnection(ConfigurationManager.AppSettings("connBTS").ToString())
            cn.Open()
            sql = "select 'BestToolService' as Azienda, DeviceID, DeviceUtenteID,  " &
                                " DeviceFunzione1, " &
                                " DeviceFunzione2,  " &
                                " DeviceFunzione3, " &
                                " DeviceFunzione4,  " &
                                " DeviceFunzione5, " &
                                " DeviceFunzione6,  " &
                                " DeviceFunzione7, " &
                                " DeviceFunzione8,  " &
                                " DeviceFunzione9, " &
                                " DeviceFunzione10,  " &
                                " DeviceFunzione11, " &
                                " SoggettiNome,  " &
                                " SoggettiCognome, " &
                                " isnull(SoggettiSoprannome, '') as SoggettiSoprannome, " &
                                " CONCAT(SoggettiNome, ' ', SoggettiCognome) as SoggettiNomeCognome,  " &
                                " SoggettiCodice, " &
                                " SoggettiCodice,  " &
                                " SoggettiEmail  " &
                                " from Device inner join Utenti on UtentiID = DeviceUtenteID inner join Soggetti on SoggettiCodice = UtentiCodiceSoggetto " &
                                " where SoggettiStato <> 'A' and UtentiStato <> 'A' and DeviceStato <> 'A' and DeviceID = '" & xxDevice & "'"
            cmd = New OleDbCommand(sql, cn)
            rs = cmd.ExecuteReader
            If rs.Read Then
                row = VerificaAutorizzazioni.NewRow()
                row.Item("DeviceID") = rs.Item("DeviceID")
                row.Item("Azienda") = rs.Item("Azienda")
                row.Item("DeviceUtenteID") = rs.Item("DeviceUtenteID")
                row.Item("DeviceFunzione1") = rs.Item("DeviceFunzione1")
                row.Item("DeviceFunzione2") = rs.Item("DeviceFunzione2")
                row.Item("DeviceFunzione3") = rs.Item("DeviceFunzione3")
                row.Item("DeviceFunzione4") = rs.Item("DeviceFunzione4")
                row.Item("DeviceFunzione5") = rs.Item("DeviceFunzione5")
                row.Item("DeviceFunzione6") = rs.Item("DeviceFunzione6")
                row.Item("DeviceFunzione7") = rs.Item("DeviceFunzione7")
                row.Item("DeviceFunzione8") = rs.Item("DeviceFunzione8")
                row.Item("DeviceFunzione9") = rs.Item("DeviceFunzione9")
                row.Item("DeviceFunzione10") = rs.Item("DeviceFunzione10")
                row.Item("DeviceFunzione11") = rs.Item("DeviceFunzione11")
                row.Item("SoggettiNome") = rs.Item("SoggettiNome")
                row.Item("SoggettiCognome") = rs.Item("SoggettiCognome")
                If rs.Item("SoggettiSoprannome").ToString <> "" Then
                    row.Item("SoggettiSoprannome") = rs.Item("SoggettiSoprannome")
                Else
                    row.Item("SoggettiSoprannome") = rs.Item("SoggettiNome")
                End If
                row.Item("SoggettiNomeCognome") = rs.Item("SoggettiNomeCognome")
                row.Item("SoggettiCodice") = rs.Item("SoggettiCodice")
                row.Item("SoggettiEmail") = rs.Item("SoggettiEmail")
                VerificaAutorizzazioni.Rows.Add(row)
            End If
            cn.Close()
        End Function

    End Class
End Namespace
