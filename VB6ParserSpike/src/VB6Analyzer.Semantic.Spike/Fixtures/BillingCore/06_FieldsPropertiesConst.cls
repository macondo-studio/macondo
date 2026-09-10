Private mCount As Long

Public Const MaxRetries As Integer = 3

Public Property Get Count() As Long
    Count = mCount
End Property

Public Property Let Count(ByVal value As Long)
    mCount = value
End Property

Private Sub Reset()
    Dim i As Integer
    mCount = 0
End Sub
