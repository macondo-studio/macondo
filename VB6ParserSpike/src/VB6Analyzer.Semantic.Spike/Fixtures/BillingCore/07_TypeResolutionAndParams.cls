Public Type Address
    Street As String
    City As String
    ZipCode As Long
End Type

Private mId As Long

Public Function CalculateTotal(ByVal subtotal As Double, ByRef taxRate As Single, Optional discount As Double = 0#) As Double
    Dim finalTax As Double
    finalTax = subtotal * taxRate
    CalculateTotal = (subtotal + finalTax) - discount
End Function

Public Sub UpdateCustomer(ByVal c As Customer, newAddress As Address)
    Dim isOk As Boolean
    isOk = True
End Sub
