Public Sub Insert(ByVal cust As Customer)
    Execute "INSERT INTO Orders (CustomerName) VALUES ('" & cust.Name & "')"
    CommitTransaction
End Sub

Public Sub Delete(ByVal orderId As Long)
    Execute "DELETE FROM Orders WHERE Id = " & orderId
    CommitTransaction
End Sub
