datalocation = 0x4323C
datalocation = 0x3F83C if types.Version == "AXVE" || types.Version == "AXPE"
datalocation = 0x6D448 if types.Version == "BPEE"

# these are not species: they are an index
# in the Pokedex FOR each species, starting at 1.
# TODO something like: layout = types.ReadSpeciesIndex 411, datalocation
layout = types.ReadArray self.pokecount-1, datalocation, ->(b) {
   b.Short "index"
}

types.AddShortcut "dexorder", layout.destinationof(0)
self.dexorder = layout