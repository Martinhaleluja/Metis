# Maths and physics diagrams
description: How to draw maths and physics on Metis's canvas — what to put down first, and in what order
domain: academic
applies-to: maths, math, geometry, geometric, triangle, triangles, polygon, square, pentagon, hexagon, angle, angles, pythagoras, pythagorean, theorem, algebra, equation, graph, area, perimeter, radius, circle, physics, force, forces, vector, vectors, velocity, acceleration, gravity, momentum, wave, waves, frequency, amplitude, wavelength, light, sound, energy, motion

You are drawing on a blank square canvas, not on the user's screen. Everything
below is about how to build a picture someone can follow while you talk.

## Where things go

The canvas runs 0-1000 across and 0-1000 down, so 500,500 is the middle. Put the
main shape near the middle at a radius of about 250-320. That leaves a margin
wide enough to hang labels off without them falling off the edge. Labels sit
just outside the shape, not on top of it — a label at the same point as a vertex
covers the very corner being named.

## Order

Draw the thing, then name its parts, then add what is being proved. A lesson on
right triangles goes: the triangle, then a label on each of the three sides,
then the right-angle marker, then whatever the theorem adds. Never open with the
detail; a label arriving before the shape it names has nothing to point at.

One shape per step. If a step needs two things drawn, it is two steps.

## Choosing a shape

- `polygon` with `diagram_sides` for anything with corners. 3 is a triangle, 4 a
  square, 5 a pentagon, 6 a hexagon. Use `diagram_rotation` when the shape needs
  to sit a particular way up — a triangle defaults to point-up.
- `circle` for circles, orbits, wheels, and anything where the roundness is the
  point.
- `line` for axes, edges, radii, and construction lines. Give both ends.
- `arrow` for anything with a direction: forces, velocity, acceleration,
  current, a ray of light. Draw it from where it acts to where it points, and
  make its length mean something — a bigger force is a longer arrow.
- `wave` for oscillation. `diagram_sides` sets how many cycles fit between the
  two ends, and `diagram_size` sets how tall the crests are. Two waves of
  different `diagram_sides` between the same two points is how you show
  frequency; same cycles and different `diagram_size` is how you show amplitude.
- `label` to name something already drawn.

## Physics diagrams

Forces act on a body, so draw the body first — usually a `polygon` or `circle` —
and then the arrows leaving it. Keep every force arrow starting at the body's
centre and pointing outward. Gravity points down the canvas, which is toward
1000 on the y axis.

For anything travelling — light, sound, a ball — the direction of travel is an
`arrow` and the thing itself may be a `wave` along the same line.

## What not to do

Do not try to draw graphs with plotted data points; there is no plotting
primitive, and a wave or a couple of lines carries the idea better. Do not draw
more than six or seven shapes in one lesson — a crowded canvas stops being a
diagram and becomes a mess. Do not use a shape as decoration; if it is on the
canvas, the narration should say what it is.
