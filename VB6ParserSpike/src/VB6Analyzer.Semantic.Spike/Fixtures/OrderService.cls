Public Sub ProcessOrder(ByVal cust As Customer)
    Dim localCust As Customer
    Set localCust = New Customer

    localCust.Validate
    localCust.Save

    Dim repo As OrderRepository
    Set repo = New OrderRepository

    repo.Insert cust
    LogMessage "Order completed successfully"
End Sub
