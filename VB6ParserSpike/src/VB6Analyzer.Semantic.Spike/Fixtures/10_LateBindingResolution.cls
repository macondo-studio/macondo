Public Sub TestLateBinding()
    Dim obj As Object
    Set obj = CreateObject("ADODB.Connection")
    obj.OpenConnection

    Dim dynVar As Variant
    dynVar.SomeDynamicMethod

    With obj
        .Execute "SELECT * FROM Table"
    End With
End Sub
