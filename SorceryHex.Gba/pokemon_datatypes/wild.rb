encounter = ->(b){
   b.ReadByte "low"
   b.ReadByte "high"
   b.ReadShort "species"
}

wild = types.FindVariableArray 0xFF, ->(b){
   b.ReadShort "bankmap"
   b.ReadShort "_"
   b.ReadNullablePointer "grass", ->(b){
      b.ReadWord "rate"
      b.ReadArray "encounters", 12, encounter
   }
   b.ReadNullablePointer "surf", ->(b){
      b.ReadWord "rate"
      b.ReadArray "encounters", 5, encounter
   }
   b.ReadNullablePointer "tree", ->(b){
      b.ReadWord "rate"
      b.ReadArray "encounters", 5, encounter
   }
   b.ReadNullablePointer "fish", ->(b){
      b.ReadWord "rate"
      b.ReadArray "encounters", 10, encounter
   }
}
