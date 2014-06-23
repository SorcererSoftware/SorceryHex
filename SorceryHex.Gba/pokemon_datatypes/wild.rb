encounter = ->(b){
   b.ByteNum "low"
   b.ByteNum "high"
   b.Species
}

wild = types.FindVariableArray 0xFF, "wpppp", ->(b){
   b.Short "bankmap"
   b.Short "_"
   b.NullablePointer "grass", ->(b){
      b.Word "rate"
      b.Array "encounters", 12, encounter
   }
   b.NullablePointer "surf", ->(b){
      b.Word "rate"
      b.Array "encounters", 5, encounter
   }
   b.NullablePointer "tree", ->(b){
      b.Word "rate"
      b.Array "encounters", 5, encounter
   }
   b.NullablePointer "fish", ->(b){
      b.Word "rate"
      b.Array "encounters", 10, encounter
   }
}
