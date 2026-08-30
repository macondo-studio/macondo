Private mName As String
Private mEmail As String

Public Property Get Name() As String
    Name = mName
End Property

Public Property Let Name(ByVal value As String)
    mName = value
End Property

Public Sub Validate()
    ' Validate customer data
End Sub

Public Sub Save()
    ' Save customer record
End Sub

Public Sub Update()
    ' Update customer record
End Sub

