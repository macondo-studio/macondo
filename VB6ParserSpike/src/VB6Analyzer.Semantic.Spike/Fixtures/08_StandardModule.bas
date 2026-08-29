Attribute VB_Name = "UtilsModule"

Public Const AppName As String = "VB6 Enterprise App"

Public Sub LogMessage(ByVal message As String)
    ' Global utility logger in standard .bas module
End Sub

Public Function FormatCurrencyValue(ByVal amount As Double) As String
    FormatCurrencyValue = "$" & amount
End Function

Private Sub InternalSecretModuleWork()
    ' Not visible outside this standard module
End Sub
