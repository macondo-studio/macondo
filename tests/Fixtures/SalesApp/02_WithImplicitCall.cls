Private Sub Save()
    With Customer
        .Update
        .Validate
    End With
End Sub
