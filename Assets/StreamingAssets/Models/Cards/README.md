# Card 3D models (Spirit Field)

## Now (default)

All cards use an **anime physical card** (`ArPhysicalCardBuilder`):

- thickness + edge chrome + Soft Vision rim  
- face = CardArt · back = official-style card back  
- unlit materials + outdoor exposure fit  

Monsters on the hologram field use **artwork billboards** until a dedicated mesh exists.

## Later (per monster)

```
StreamingAssets/Models/Cards/{cardId}.obj
StreamingAssets/Models/Cards/{cardId}.glb   (future)
```

Example: Dark Magician → `46986414.obj`.

When present and `PreferMeshWhenAvailable` is on, the arena swaps the billboard for that mesh (still anime-lit via unlit art materials).

## Pipeline tips

1. Export Y-up, ~1 unit tall, mobile polycount.  
2. Style: stylized / anime (match 2D art), not photoreal.  
3. Dynamic behaviors (summon rise, attack lunge) layer on top of the mesh — art team + code.
