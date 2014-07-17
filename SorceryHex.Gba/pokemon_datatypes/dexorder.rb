types.WaitFor "_setup"

_dexorder = 0x4323C
_dexorder = 0x3F83C if types.Version == "AXVE" || types.Version == "AXPE"
_dexorder = 0x6D448 if types.Version == "BPEE"

# these are not species: they are an index
# in the Pokedex FOR each species, starting at 1.
# TODO something like: layout = types.ReadSpeciesIndex 411, datalocation
self.dexorder = types.ReadArray pokecount-1, _dexorder, ->(b) {
   b.Short "index"
}

types.AddShortcut "dexorder", dexorder[0].Location