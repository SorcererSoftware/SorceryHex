wild = types.FindVariableArray(0xFF, ->(b){
   b.ReadShort("bankmap")
   b.ReadShort("_")
   b.ReadNullablePointer("grass", ->(b){
      b.ReadWord("rate")
      b.ReadArray("encounters", 12, ->(b){
         b.ReadByte("low")
         b.ReadByte("high")
         b.ReadShort("species")
      })
   })
   b.ReadNullablePointer("surf", ->(b){
      b.ReadWord("rate")
      b.ReadArray("encounters", 5, ->(b){
         b.ReadByte("low")
         b.ReadByte("high")
         b.ReadShort("species")
      })
   })
   b.ReadNullablePointer("tree", ->(b){
      b.ReadWord("rate")
      b.ReadArray("encounters", 5, ->(b){
         b.ReadByte("low")
         b.ReadByte("high")
         b.ReadShort("species")
      })
   })
   b.ReadNullablePointer("fish", ->(b){
      b.ReadWord("rate")
      b.ReadArray("encounters", 10, ->(b){
         b.ReadByte("low")
         b.ReadByte("high")
         b.ReadShort("species")
      })
   })
})
