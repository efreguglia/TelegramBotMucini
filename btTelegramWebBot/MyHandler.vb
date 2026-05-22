Imports System.Net.Http
Imports System.Threading
Imports System.Threading.Tasks

Namespace btTelegramWebBot
    Public Class MyHandler
        Inherits DelegatingHandler

        Protected Async Overrides Function SendAsync(ByVal request As HttpRequestMessage, ByVal cancellationToken As CancellationToken) As Task(Of HttpResponseMessage)
            If request.Content IsNot Nothing Then
                request.Properties.Add("rawpostdata", request.Content.ReadAsStringAsync().Result)
            End If

            Return Await MyBase.SendAsync(request, cancellationToken)
        End Function
    End Class
End Namespace
