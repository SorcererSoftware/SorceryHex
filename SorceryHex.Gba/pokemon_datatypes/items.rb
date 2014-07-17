self.items = types.FindVariableArray "wwwwwpwpwpw", ->(b){
   b.String 14, "name"
   b.Short "id"
   b.Short "itemtype"
   b.Short "value" # such as potion healing 20
   b.StringPointer "description"
   b.Short "unknown"
   b.Short "unknown"
   b.NullablePointer "code"
   b.Word "unknown"
   b.NullablePointer "code2"
   b.Word "variable"
}

types.AddShortcut "items", items.destination
self.items = items.data