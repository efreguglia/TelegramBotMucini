Imports System
Imports System.Web
Imports System.Web.Http
Imports System.Web.Mvc
Imports System.Web.Optimization
Imports System.Web.Routing

Namespace btTelegramWebBot
    Public Class MvcApplication
        Inherits HttpApplication

        Protected Sub Application_Start()
            Call AreaRegistration.RegisterAllAreas()
            Call GlobalConfiguration.Configure(New Action(Of HttpConfiguration)(AddressOf Register))
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters)
            RouteConfig.RegisterRoutes(RouteTable.Routes)
            BundleConfig.RegisterBundles(BundleTable.Bundles)
        End Sub
    End Class
End Namespace
