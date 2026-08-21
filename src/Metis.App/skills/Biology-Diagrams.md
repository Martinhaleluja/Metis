# Biology diagrams
description: How to draw biology on Metis's canvas — structures from the outside in, one part at a time
domain: academic
applies-to: biology, biological, cell, cells, organelle, membrane, nucleus, mitochondria, mitochondrion, chloroplast, cytoplasm, ribosome, vacuole, dna, chromosome, gene, photosynthesis, respiration, osmosis, diffusion, enzyme, protein, organism, ecosystem, food chain, plant, leaf, root, blood, heart, lung, neuron

You are drawing on a blank square canvas, not on the user's screen. Everything
below is about how to build a picture someone can follow while you talk.

## Where things go

The canvas runs 0-1000 across and 0-1000 down, so 500,500 is the middle. A
structure's outer boundary goes in the middle at a radius of about 320-380, big
enough that the parts inside it have room to be told apart.

## Order: outside in

Draw the container first, then what is inside it, then name the parts. A plant
cell goes: the cell wall as a large `circle`, then the nucleus as a smaller
circle inside it, then a chloroplast, then a vacuole — each its own step — and
then a `label` on each one. Someone watching should be able to stop at any point
and still be looking at a coherent picture.

Put internal parts off-centre so they do not sit on top of each other. Think of
where there is space: a nucleus at 420,430, a vacuole at 600,560, a chloroplast
at 380,620. Give each a radius well under the container's, around 70-120.

One part per step. If a step needs two things drawn, it is two steps.

## Choosing a shape

- `circle` for cells, nuclei, vacuoles, and most organelles. Biology is mostly
  round, and a circle at a small radius reads as an organelle without needing
  detail it cannot have.
- `polygon` for anything angular — a plant cell wall reads better as a hexagon
  than a circle, and `diagram_sides` of 5 or 6 does that.
- `arrow` for movement and flow: water into a root, blood through a heart, sugar
  out of a leaf, energy along a food chain. Draw it in the direction the thing
  actually moves.
- `line` for boundaries and divisions, like a membrane between two regions.
- `wave` for light arriving at a leaf, which is the one place in school biology
  a wave earns its place.
- `label` to name a part that is already drawn.

## Processes

For a process rather than a structure — photosynthesis, respiration, a food
chain — draw the participants as shapes and the process as arrows between them.
Photosynthesis is the leaf, then a `wave` for the light arriving, then arrows
for water up and carbon dioxide in and oxygen out, each labelled. The arrows
carry the process; the shapes only give them something to run between.

## What not to do

Do not attempt anatomical accuracy — these are a handful of circles and arrows,
and a diagram that tries to look like a real cell will only look wrong. Aim for
what a textbook draws on a blackboard. Do not draw more than six or seven shapes
in one lesson. Do not put a label at the exact centre of the shape it names when
the shape is small; offset it so both stay readable.
