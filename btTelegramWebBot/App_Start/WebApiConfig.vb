Imports System.Web.Http

Namespace btTelegramWebBot
    Public Module WebApiConfig
        Public Sub Register(ByVal config As HttpConfiguration)
            ' Servizi e configurazione dell'API Web
            ' Route dell'API Web
            config.MapHttpAttributeRoutes()
            config.Routes.MapHttpRoute(name:="DefaultApi", routeTemplate:="api/{controller}/{id}", defaults:=New With {
                .id = RouteParameter.Optional
            })
            config.MessageHandlers.Add(New MyHandler())
        End Sub
    End Module
End Namespace
