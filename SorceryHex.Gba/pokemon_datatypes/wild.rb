encounter = ->(b){
   low = b.ByteNum "low"
   high = b.ByteNum "high"
   b.Assert (low<=high && high<=100), "pokemon levels range from 1-100"
   b.Species
}

self.wild = types.FindVariableArray 0xFF, "wpppp", ->(b){
   b.Short "bankmap"
   b.Unused 2
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
