Private mOrderCount As Long

Public Sub ProcessOrder(ByVal cust As Customer)
    Dim localCust As Customer
    Set localCust = New Customer

    ' 1. Unqualified call to standard module .bas
    LogMessage "Starting order processing"

    ' 2. Qualified call on local variable
    localCust.Save

    ' 3. Qualified call on parameter
    cust.Update

    ' 4. Me call
    Me.InternalClassMethod

    ' 5. Unqualified internal method call
    InternalClassMethod

    ' 6. Contextual With block
    With cust
        .Save
    End With
End Sub

Public Sub InternalClassMethod()
    mOrderCount = mOrderCount + 1
End Sub
