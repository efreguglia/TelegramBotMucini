Imports System.Web.Http.Controllers
Imports System.Runtime.CompilerServices

Namespace btTelegramWebBot.Controllers
    Public Module ControllerExtensions
        <Extension()>
        Public Function GetRawPostData(ByVal requestContext As HttpControllerContext) As String
            Return CStr(requestContext.Request.Properties("rawpostdata"))
        End Function
    End Module
End Namespace
