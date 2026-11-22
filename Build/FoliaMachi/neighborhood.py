"""Blender geometry for the streets beyond Machi's original twelve-metre sets.

All dimensions use the upstream Y-up metre coordinates. The authored building
fronts face the street, and roof and facade edges have physical thickness for
the path tracer.
"""

from math import cos, pi, sin, sqrt
from random import Random


SET_EDGE = 6.0
STREET_EXTENT = 180.0
KOWLOON_SHOP_HEIGHT = 2.34
KOWLOON_STOREY_HEIGHT = 2.28


class Facade:
    """Coordinates along a facade, up from its foot, and out towards the street."""

    def __init__(self, kit, name, x, y, z, facing="front"):
        self.kit = kit
        self.name = name
        self.x, self.y, self.z = x, y, z
        self.facing = facing

    def point(self, u, y, depth):
        if self.facing == "right":
            return self.x + depth, self.y + y, self.z - u
        if self.facing == "left":
            return self.x - depth, self.y + y, self.z + u
        if self.facing == "rear":
            return self.x - u, self.y + y, self.z - depth
        return self.x + u, self.y + y, self.z + depth

    def box(self, label, u, y, depth, width, height, thickness, material, bevel=0):
        size = (width, height, thickness)
        if self.facing in ("right", "left"):
            size = (thickness, height, width)
        self.kit.box(f"{self.name}/{label}", self.point(u, y, depth), size, material, bevel)

    def span(self, label, u0, u1, y0, y1, d0, d1, material, bevel=0):
        self.box(label, (u0 + u1) / 2, (y0 + y1) / 2, (d0 + d1) / 2,
                 u1 - u0, y1 - y0, d1 - d0, material, bevel)

    def mesh(self, label, vertices, faces, material, smooth=False):
        self.kit.mesh(f"{self.name}/{label}", [self.point(*v) for v in vertices], faces, material, smooth)

    def rod(self, label, start, end, radius, material, sides=8):
        self.kit.rod(f"{self.name}/{label}", self.point(*start), self.point(*end), radius, material, sides)

    def panel(self, label, u, y, d, width, height, material):
        left, right = u - width / 2, u + width / 2
        bottom, top = y - height / 2, y + height / 2
        self.mesh(label, [(left, bottom, d), (right, bottom, d), (right, top, d), (left, top, d)],
                  [(0, 1, 2, 3)], material)

    def ceiling_light(self, u, y, d, width, depth, housing, emitter):
        self.box("Ceiling light housing", u, y + .028, d, width + .035, .055, depth + .035, housing, .009)
        left, right = u - width / 2, u + width / 2
        rear, front = d - depth / 2, d + depth / 2
        self.mesh("Ceiling diffuser", [(left, y, rear), (right, y, rear), (right, y, front), (left, y, front)],
                  [(0, 1, 2, 3)], emitter)


def _surface(kit, label, x0, x1, z0, z1, y, material):
    vertices = [(x0, y, z0), (x0, y, z1), (x1, y, z1), (x1, y, z0)]
    kit.mesh(label, vertices, [(0, 1, 2, 3)], material)


def _curbs(kit, label, x0, x1, z0, z1, y, material):
    along_x = x1 - x0 > z1 - z0
    length = x1 - x0 if along_x else z1 - z0
    count = max(1, round(length / .86))
    for i in range(count):
        t = (i + .5) / count
        x = x0 + (x1 - x0) * (t if along_x else .5)
        z = z0 + (z1 - z0) * (.5 if along_x else t)
        size = (length / count - .012, .125, z1 - z0) if along_x else (x1 - x0, .125, length / count - .012)
        kit.box(label, (x, y - .0625, z), size, material, .013, segments=1)


def _street(kit, district):
    # Match the exported road and lot at the boundary. The miniature's slightly
    # rounded corners remain covered by a 3 cm overlap below the existing surface.
    asphalt = kit.mat("Existing asphalt continuation", "#2a303c", .733, kind=4)
    concrete = kit.mat("Existing concrete continuation", "#4b5363", .733, kind=9)
    curb = kit.mat("Curb stones", "#626976", .83, kind=15)
    paint = kit.mat("Worn road paint", "#9aa3a9", .7)
    seal = kit.mat("Asphalt repairs", "#262b34", .83, kind=4)
    edge, end = SET_EDGE, STREET_EXTENT
    is_alley = district == "kowloon"
    # Leave a square opening beneath the original static streets.
    for limits in [(-end, -edge + .03, -end, end), (edge - .03, end, -end, end),
                   (-edge + .03, edge - .03, -end, -edge + .03),
                   (-edge + .03, edge - .03, edge - .03, end)]:
        _surface(kit, "Continuous district street", *limits, .0215 if is_alley else .0015,
                 concrete if is_alley else asphalt)

    if district == "konbini":
        for limits in [(-end, -6, -end, 2.2), (-6, 2.2, -end, -6), (5.5, end, -end, -6),
                       (5.97, end, -6, 2.2), (-end, -6, 5.5, end),
                       (-6, 2.2, 5.97, end), (5.97, end, 5.5, end), (5.5, 5.97, 5.97, end)]:
            _surface(kit, "Raised neighborhood pavement", *limits, .1215, concrete)
        for limits in [(-14.5, -6, 2.04, 2.2), (-14.5, -6, 5.5, 5.66),
                       (2.04, 2.2, -14.5, -6), (5.5, 5.66, -14.5, -6),
                       (2.04, 2.2, 6, 15.5), (5.5, 5.66, 6, 15.5),
                       (6, 15.5, 2.04, 2.2), (6, 15.5, 5.5, 5.66)]:
            _curbs(kit, "Neighborhood curb", *limits, .133, curb)
        for shift in (-1, 1):
            start, stop = sorted((6 * shift, 14.2 * shift))
            for lane in (2.44, 5.26):
                _surface(kit, "Continuing edge stripe", start, stop, lane - .042, lane + .042, .006, paint)
                _surface(kit, "Continuing edge stripe", lane - .042, lane + .042, start, stop, .006, paint)
    elif district == "station":
        for limits in [(-19, -6, -17, 2.19), (-6, 18, -17, -6), (6, 18, -6, 2.19),
                       (-18, 18, 6.8, 7.65)]:
            _surface(kit, "Slightly raised parking pavement", *limits, .02, concrete)
        _curbs(kit, "Parking edge", -18, -6, 2.19, 2.35, .121, curb)
        _curbs(kit, "Parking edge", 6, 18, 2.19, 2.35, .121, curb)
        _curbs(kit, "Far roadside curb", -18, 18, 6.8, 6.98, .12, curb)
        _surface(kit, "Far roadside walk", -18, 18, 6.98, 7.65, .12, concrete)
        for shift in (-1, 1):
            start, stop = sorted((6 * shift, 18 * shift))
            for line in (2.57, 5.80):
                _surface(kit, "Highway edge stripe", start, stop, line - .04, line + .04, .005, paint)
            for i in range(8):
                x = (7.3 if shift == 1 else -6.7) + shift * i * 1.4
                _surface(kit, "Highway lane dash", x - .40, x + .40, 4.125, 4.225, .005, paint)
        _surface(kit, "Highway patched seam", -17, 17, 6.48, 6.59, .004, seal)
    return curb, paint


def _window(front, u, y, width, height, frame, sill, pane, curtains=None, divisions=2, depth=0):
    front.box("Window reveal", u, y, depth + .019, width + .14, height + .12, .065, frame, .015)
    front.panel("Window glass", u, y, depth + .054, width, height, pane)
    if curtains is not None:
        for sign in (-1, 1):
            front.box("Curtain edge", u + sign * width * .426, y, depth + .056, width * .142, height, .014, curtains)
    for division in range(1, divisions):
        x = u + width * (division / divisions - .5)
        front.box("Window mullion", x, y, depth + .066, .045, height + .045, .039, frame)
    front.box("Window ledge", u, y - height / 2 - .056, depth + .09, width + .21, .09, .23, sill, .013)


def _door(front, u, bottom, width, height, frame, panel, glass=None):
    front.box("Door surround", u, bottom + height / 2, .032, width + .11, height + .12, .10, frame, .011)
    front.box("Door leaf", u, bottom + height / 2, .087, width, height, .045, panel, .009)
    front.box("Lever", u + width * .32, bottom + height * .49, .132, .11, .030, .068, frame, .006)
    if glass is not None:
        front.panel("Door window", u, bottom + height * .73, .114, width * .68, height * .30, glass)


def _roof(front, width, depth, eave, rise, roof, flashing, profile="gable"):
    """A roof with a closed eave and ridge, above an occupied upper floor."""
    left, right, front_edge, rear = -width / 2 - .18, width / 2 + .18, .40, -depth - .18
    ridge = -depth * .54 if profile == "saltbox" else -depth * .48
    inset = min(width * .21, .82) if profile == "hip" else 0
    vertices = [(left, eave, front_edge), (right, eave, front_edge), (right, eave, rear), (left, eave, rear),
                (left + inset, eave + rise, ridge), (right - inset, eave + rise, ridge),
                (left, eave - .14, front_edge), (right, eave - .14, front_edge),
                (right, eave - .14, rear), (left, eave - .14, rear)]
    faces = [(0, 1, 5, 4), (4, 5, 2, 3), (3, 0, 4), (1, 2, 5),
             (6, 7, 1, 0), (7, 8, 2, 1), (8, 9, 3, 2), (9, 6, 0, 3), (9, 8, 7, 6)]
    front.mesh("Pitched standing seam roof", vertices, faces, roof)
    front.rod("Ridge cap", (left + inset, eave + rise + .025, ridge),
              (right - inset, eave + rise + .025, ridge), .045, flashing)
    front.rod("Street gutter", (left, eave - .057, front_edge + .025),
              (right, eave - .057, front_edge + .025), .062, flashing)
    front.rod("Gutter return", (right - .05, eave - .057, front_edge + .025),
              (right - .05, eave - .20, .11), .037, flashing)
    front.rod("Gutter downpipe", (right - .05, eave - .20, .11), (right - .05, .17, .11), .034, flashing)
    count = int(width / .31)
    for i in range(count):
        fraction = (i + .5) / count
        u = left + fraction * (right - left)
        ridge_u = left + inset + fraction * (right - left - inset * 2)
        # Seam centres follow the two closed slopes. Seat each profile in
        # the metal and stop it inside the eave and ridge perimeter.
        for edge in (front_edge, rear):
            points = [(u + (ridge_u - u) * t, eave + rise * t + .007, edge + (ridge - edge) * t)
                      for t in (.018, .988)]
            front.rod("Roof seam", *points, .013, flashing, sides=4)


def _flat_roof(front, width, depth, y, fascia, roofing, returns=True):
    front.box("Roof slab", 0, y, -depth / 2, width + .16, .16, depth + .16, roofing, .025)
    front.box("Front parapet", 0, y + .23, .02, width + .16, .36, .17, fascia, .024)
    front.box("Rear parapet", 0, y + .23, -depth - .02, width + .16, .36, .17, fascia, .024)
    if returns:
        for side in (-1, 1):
            front.box("Parapet return", side * width / 2, y + .23, -depth / 2, .17, .36, depth, fascia, .024)


def _air_conditioner(front, u, y, depth, casing, metal):
    front.box("AC cabinet", u, y, depth, .57, .38, .35, casing, .035)
    front.box("AC front recess", u, y, depth + .178, .48, .28, .027, metal, .013)
    # Horizontal louvres sit on top of the dark recess.
    for i in range(5):
        front.box("AC louvre", u, y + (i - 2) * .045, depth + .199, .46, .014, .021, casing)
    for shift in (-.18, .18):
        front.box("AC bracket", u + shift, y - .23, depth - .043, .035, .10, .37, metal)


def _foliage(kit, colors):
    bark = kit.mat("Shrub stems", "#493b2b", .89)
    leaves = [kit.mat(f"Evergreen leaf tone {i + 1}", color, .67 + .07 * i)
              for i, color in enumerate(colors)]
    return bark, leaves


def _shrub(kit, label, x, y, z, scale, foliage, rng, leaf_count=46):
    bark, leaves = foliage
    batches = [([], []) for _ in leaves]
    turn = rng.uniform(0, 2 * pi)
    for branch in range(6):
        angle = turn + branch * 2.39996
        reach = rng.uniform(.078, .134) if branch else .016
        branch_x, branch_z = cos(angle) * reach, sin(angle) * reach
        height = rng.uniform(.18, .30) if branch else .14
        radius = rng.uniform(.104, .142)
        kit.rod(f"{label}/Woody stem", (x, y - .009 * scale, z),
                (x + branch_x * scale, y + height * scale, z + branch_z * scale),
                .0055 * scale, bark, sides=6)
        for index in range(leaf_count):
            # Scatter across an irregular crown, with some leaves deeper in the branch.
            azimuth = angle + index * 2.39996 + rng.uniform(-.25, .25)
            rise = 1.84 * (index + .5) / leaf_count - .88
            spread = radius * sqrt(1 - rise * rise) * rng.uniform(.63, 1)
            cx = branch_x + spread * cos(azimuth)
            cy = height + rise * radius
            cz = branch_z + spread * sin(azimuth)
            heading = azimuth + rng.uniform(-.84, .84)
            along_x, along_z = cos(heading), sin(heading)
            lift = rng.uniform(-.38, .72)
            length = rng.uniform(.048, .097)
            half_width = length * rng.uniform(.17, .29)
            tone = rng.choices((0, 1, 2), (2, 5, 3) if rise > .0 else (5, 4, 1))[0]
            vertices, faces = batches[tone]
            start = len(vertices)
            # Each leaf has a raised midrib and a slight downturn at the tip.
            for along, side, bend in ((-.45, 0, 0), (.05, 1, 0), (.10, 0, .29),
                                      (.05, -1, 0), (.55, 0, -.20)):
                path, across = along * length, side * half_width
                vertices.append((x + scale * (cx + path * along_x + across * along_z),
                                 y + scale * (cy + path * lift + bend * half_width),
                                 z + scale * (cz + path * along_z - across * along_x)))
            faces.extend([(start, start + 2, start + 1), (start, start + 3, start + 2),
                          (start + 2, start + 4, start + 1), (start + 2, start + 3, start + 4)])
    for (vertices, faces), material in zip(batches, leaves):
        kit.mesh(f"{label}/Evergreen leaves", vertices, faces, material, smooth=True)


def _planter(kit, label, x, z, width, depth, y, concrete, soil, foliage, seed):
    rim = .055
    kit.box(f"{label}/Concrete base", (x, y + .144, z), (width, .288, depth), concrete, .020)
    for side in (-1, 1):
        kit.box(f"{label}/Long planter rim", (x, y + .315, z + side * (depth - rim) / 2),
                (width, .09, rim), concrete, .011)
        kit.box(f"{label}/End planter rim", (x + side * (width - rim) / 2, y + .315, z),
                (rim, .09, depth - rim * 2 + .002), concrete, .011)
    _surface(kit, f"{label}/Soil", x - width / 2 + rim - .001, x + width / 2 - rim + .001,
             z - depth / 2 + rim - .001, z + depth / 2 - rim + .001, y + .324, soil)
    rng = Random(seed)
    along_x = width > depth
    length = max(width, depth)
    count = max(1, round((length - .16) / .335))
    for index in range(count):
        along = ((index + .5) / count - .5) * (length - .10) + rng.uniform(-.025, .025)
        across = rng.uniform(-.032, .032)
        dx, dz = (along, across) if along_x else (across, along)
        _shrub(kit, label, x + dx, y + .323, z + dz, rng.uniform(.76, 1.0), foliage, rng)


def window_plant(kit):
    clay = kit.mat("Weathered terracotta pot", "#724c41", .87)
    soil = kit.mat("Window pot soil", "#352c23", .96)
    profile = [(0, .032), (.043, .049), (.045, .054), (.055, .054), (.055, .045), (.039, .041)]
    sides = 12
    vertices = [(radius * cos(i * 2 * pi / sides), height, radius * sin(i * 2 * pi / sides))
                for height, radius in profile for i in range(sides)]
    faces = [tuple(range(sides))]
    for ring in range(len(profile) - 1):
        lower = ring * sides
        upper = lower + sides
        for i in range(sides):
            j = (i + 1) % sides
            faces.append((lower + i, upper + i, upper + j, lower + j))
    kit.mesh("Window pot", vertices, faces, clay, smooth=True)
    kit.rod("Soil in pot", (0, .039, 0), (0, .046, 0), .043, soil, sides)
    foliage = _foliage(kit, ("#325233", "#52723e", "#668a4d"))
    _shrub(kit, "Window plant", 0, .044, 0, .255, foliage, Random(10), leaf_count=16)


def _bollard(kit, label, x, y, z, paint, metal):
    kit.rod(label, (x, y, z), (x, y + .67, z), .064, metal)
    kit.rod("Bollard reflective sleeve", (x, y + .52, z), (x, y + .61, z), .066, paint)


def _parking_pocket(kit, label, x0, x1, z0, z1, y, stalls, slab, curb, paint):
    _surface(kit, f"{label}/Paved parking pocket", x0, x1, z0, z1, y, slab)
    # Keep the street end open and delineate the stalls at the rear of the lot.
    dx = (x1 - x0 - .64) / stalls
    for index in range(stalls + 1):
        x = x0 + .32 + index * dx
        _surface(kit, f"{label}/Stall divider", x - .03, x + .03, z0 + .22, z1 - .52, y + .006, paint)
    for index in range(stalls):
        x = x0 + .32 + (index + .5) * dx
        kit.box(f"{label}/Parking stop", (x, y + .068, z1 - .50), (.66, .136, .18), curb, .020)


def _side_lintel(front, u, y, width, trim, housing, emitter, fascia=None):
    front.box("Shallow rain hood", u, y + .065, .20, width, .105, .35, trim, .017)
    front.ceiling_light(u, y, .24, min(.7, width * .46), .075, housing, emitter)
    if fascia:
        front.panel("Rain hood fascia lens", u, y + .065, .377, width * .78, .047, fascia)


def _balcony(front, width, floor, depth, slab, rail, posts, stair=None):
    front.span("Walkway slab", -width / 2, width / 2, floor - .15, floor, -.05, depth, slab, .020)
    intervals = [(-width / 2, width / 2)]
    if stair is not None:
        intervals = [(-width / 2, stair - .55), (stair + .55, width / 2)]
    for left, right in intervals:
        u = (left + right) / 2
        front.box("Balcony handrail", u, floor + .95, depth - .075, right - left, .057, .068, rail, .012)
        front.box("Balcony bottom rail", u, floor + .15, depth - .075, right - left, .045, .054, rail)
    for index in range(posts + 1):
        u = (index / posts - .5) * (width - .08)
        if stair is None or abs(u - stair) >= .55:
            front.box("Balcony railing picket", u, floor + .55, depth - .075, .036, .80, .043, rail)
    for side in (-1, 1):
        front.box("Balcony railing return", side * (width / 2 - .05), floor + .95, depth / 2, .06, .057, depth, rail)


def _japanese_house(front, width, depth, eave, rise, design, materials, shop=None):
    m = materials
    wall = m[design]
    trim, lamp, upstairs = shop or (m["edge" if design == "tile" else "door"], None, None)
    front.span("Plinth", -width / 2, width / 2, -.04, .27, -depth, .045, m["plinth"], .021)
    front.span("Occupied house", -width / 2, width / 2, .24, eave - .13, -depth, 0, wall, .038)
    _roof(front, width, depth, eave, rise, m["roof" if design == "plaster" else "roofBlue"], m["gutter"],
          {"plaster": "hip", "cedar": "saltbox", "tile": "gable"}[design])
    lower = m["wood"] if design != "cedar" else m["mahogany"]
    front.box("First floor lintel", 0, 2.49, .032, width, .135, .16, lower, .013)
    for sign in (-1, 1):
        front.box("Street corner timber", sign * (width / 2 - .105), 1.35, .036, .14, 2.27, .15, lower, .013)
    if design == "cedar":
        # A narrow residence with a deep shoji bay, visible timber slats, and a
        # small entrance awning, compressed to the scale of the convenience store.
        _door(front, -width * .27, .27, .80, 1.98, m["gutter"], m["wood"], m["glass"])
        _window(front, width * .18, 1.36, width * .40, 1.14, m["wood"], m["plinth"], m["glass"], divisions=3)
        for i in range(9):
            u = width * .18 + (i - 4) * width * .042
            front.box("Timber bay lattice", u, 1.36, .24, .034, 1.12, .044, m["mahogany"])
        front.box("Timber bay cap", width * .18, 1.99, .17, width * .51, .10, .32, m["mahogany"], .015)
        front.box("Entrance step", -width * .27, .14, .26, 1.02, .21, .52, m["plinth"], .024)
    else:
        _door(front, width * .31, .27, .81, 2.04, lower, m["door"], m["glass"])
        _window(front, -width * .17, 1.37, width * .48, 1.52, lower, m["plinth"],
                m["warmWindow"], m["curtains"], divisions=3)
        front.box("Head casing", -.07, 2.44, .44, width + .06, .14, .84, m["roof"], .022)
        front.box("Enamel awning fascia", -.07, 2.39, .88, width + .08, .14, .10, trim, .018)
        front.ceiling_light(-width * .24, 2.345, .56, .55, .067, m["gutter"], m["porch"])
        if lamp:
            front.panel("Backlit fascia lens", -.07, 2.391, .932, width * .74, .045, lamp)
            front.box("Glazed shopfront tiles", -width * .17, .47, .046, width * .56, .34, .10, trim, .014)
        for i in range(3):
            front.box("Noren panel", -width * .27 + i * .30, 2.22, .84, .278, .28, .036, m["linen"], .008)
    front.box("Second floor cladding band", 0, 2.79, .033, width, .24, .11, m["plinth"])
    if design == "plaster":
        _balcony(front, width * .64, 2.95, .65, m["plinth"], m["gutter"], posts=10)
        _window(front, -.27, 3.62, width * .43, 1.18, lower, m["plinth"], upstairs or m["warmWindow"], m["curtains"])
        _air_conditioner(front, width * .34, 3.32, .22, m["ac"], m["gutter"])
    elif design == "cedar":
        _window(front, 0, 3.30, width * .57, .86, lower, m["plinth"], m["glass"], divisions=3)
        for sign in (-1, 1):
            front.box("Raised shutter", sign * width * .365, 3.3, .07, width * .10, .94, .09, lower, .012)
    else:
        for sign in (-1, 1):
            _window(front, sign * width * .245, 3.60, width * .30, 1.03, lower, m["plinth"],
                    (upstairs or m["glass"]) if sign < 0 else m["glass"])
        _air_conditioner(front, 0, 3.0, .225, m["ac"], m["gutter"])


def _japanese_return(front, width, m, design):
    side_trim = m["mahogany"] if design == "cedar" else m["plinth"]
    for sign in (-1, 1):
        front.box("Corner covering board", sign * (width / 2 - .04), 2.08, .029, .085, 3.67, .095, side_trim)
    front.box("Continuous second-floor sill band", 0, 2.81, .035, width, .15, .14, side_trim, .011)
    front.box("Lower side cladding band", 0, .57, .031, width, .33, .11, side_trim, .010)
    _window(front, -width * .20, 1.44, width * .31, .87, m["wood"], m["plinth"], m["glass"], divisions=2)
    _window(front, .02, 3.41, width * .52, .91, m["wood"], m["plinth"], m["warmWindow"], m["curtains"], divisions=3)
    _side_lintel(front, .02, 3.96, width * .62, m["roof"], m["gutter"], m["porch"])
    _air_conditioner(front, width * .25, 2.24, .183, m["ac"], m["gutter"])


def _japanese_annex(front, width, depth, m, tinted=False, fascia=None):
    front.span("Small neighborhood shop", -width / 2, width / 2, 0, 2.27, -depth, 0,
               m["plaster" if tinted else "tile"], .030)
    _roof(front, width, depth, 2.36, .39, m["roof" if tinted else "roofBlue"], m["gutter"], "saltbox")
    _door(front, width * .29, .075, .67, 1.88, m["wood"], m["door"])
    _window(front, -width * .17, 1.33, width * .44, 1.31, m["wood"], m["plinth"],
            m["warmWindow"], m["curtains"], divisions=3)
    _side_lintel(front, 0, 2.14, width * .93, m["edge"], m["gutter"], m["porch"], fascia)
    for index in range(5):
        u = -width * .41 + index * width * .113
        front.box("Faded cotton noren flap", u, 2.025, .342, width * .096, .17, .031, m["linen"], .006)
    if front.facing == "rear":
        back = Facade(front.kit, front.name + " rear wall", *front.point(0, 0, -depth))
        _window(back, -.32, 1.56, width * .30, .60, m["wood"], m["plinth"],
                m["warmWindow"], m["curtains"], divisions=2)
        gable = Facade(front.kit, front.name + " service gable",
                        *front.point(-width / 2, 0, -depth / 2), "right")
        _window(gable, -.26, 1.54, .83, .53, m["wood"], m["plinth"], m["warmWindow"], m["curtains"])


def _konbini(kit, curb, paint):
    m = {
        "plaster": kit.mat("Japanese oyster stucco", "#9c9182", .86, kind=15),
        "cedar": kit.mat("Dark cedar wallboards", "#624839", .80),
        "tile": kit.mat("Blue-grey small format tiles", "#706f70", .76, kind=14),
        "plinth": kit.mat("Wash aggregate foundations", "#766f68", .86, kind=15),
        "roof": kit.mat("Charcoal standing seam sheet", "#39434b", .48, .30, kind=16),
        "roofBlue": kit.mat("Weathered indigo metal roofing", "#40525e", .53, .35, kind=16),
        "gutter": kit.mat("Painted galvanised iron", "#3d444a", .53, .34),
        "wood": kit.mat("Oiled dark joinery", "#433027", .53),
        "mahogany": kit.mat("Red-brown cedar trim", "#734835", .62),
        "door": kit.mat("Soft jade shop paint", "#465e5a", .61),
        "edge": kit.mat("Dusty red awning paint", "#80584c", .60),
        "glass": kit.mat("Unlit oxidised-blue glazing", "#293a43", .27, .11),
        "linen": kit.mat("Indigo noren fabric", "#3e4e68", .89),
        "curtains": kit.mat("Linen curtain cloth", "#a69b86", .92),
        "ac": kit.mat("Ivory enamel appliance", "#908b80", .57, .11),
        "warmWindow": kit.mat("Warm Japanese interior behind glass", "#77604a", .51, emission=(.38, .24, .13)),
        "porch": kit.mat("Soft warm shop downlight", "#cab7a0", .42, emission=(3.4, 2.42, 1.48)),
        "soil": kit.mat("Damp planter soil", "#2f2921", .94),
    }
    denim = kit.mat("Konbini/Cobalt shop enamel", "#2266ab", .42, .14, kind=14)
    persimmon = kit.mat("Konbini/Persimmon shop enamel", "#c45632", .43, .13, kind=14)
    jade = kit.mat("Konbini/Jade awning enamel", "#21876c", .42, .13)
    mint = kit.mat("Konbini/Mint neighborhood fascia lens", "#78eed1", .43, emission=(.48, 2.65, 1.85))
    apricot = kit.mat("Konbini/Apricot neighborhood fascia lens", "#ffd17b", .42, emission=(3.7, 2.02, .58))
    blue_window = kit.mat("Konbini/Cool upstairs window", "#638ea1", .52, emission=(.27, .64, .88))
    rose_window = kit.mat("Konbini/Rose upstairs window", "#a7787d", .53, emission=(.91, .42, .46))
    shopfronts = {
        "Corner sash shop": (denim, mint, blue_window),
        "Tile-front shop": (persimmon, apricot, rose_window),
        "Frame shop at the end of the right street": (jade, mint, blue_window),
        "House across the western junction": (persimmon, apricot, rose_window),
    }
    # The tall parts sit on the far side of the store. The corner across the
    # two traffic lanes stays open at eye level to preserve all three faces.
    houses = [
        ("Corner sash shop", -6.55, -3.20, "right", 3.50, 3.35, 4.42, .63, "plaster"),
        ("Cedar house", -6.65, .39, "right", 3.25, 3.45, 3.98, .84, "cedar"),
        ("Tile-front shop", -.12, -6.66, "front", 3.80, 3.45, 4.34, .61, "tile"),
        ("Rear cedar house", -4.06, -6.73, "front", 3.54, 3.22, 3.98, .84, "cedar"),
        ("Second-row plaster house", -12.14, -.16, "front", 3.29, 3.74, 4.32, .58, "plaster"),
        ("Frame shop at the end of the right street", 10.99, -2.02, "front", 3.47, 3.91, 4.08, .77, "tile"),
        ("West lane hardware shop", -14.78, -6.57, "right", 4.66, 4.01, 4.96, .63, "plaster"),
        ("West lane timber homes", -14.40, -.91, "right", 4.57, 3.92, 4.72, .67, "cedar"),
        ("House across the western junction", -13.77, 8.16, "right", 4.59, 4.35, 4.98, .73, "tile"),
        ("Laundry behind the store", -9.53, -12.79, "front", 4.77, 4.09, 4.72, .67, "cedar"),
        ("North lane corner house", -3.62, -12.91, "front", 4.46, 3.74, 4.94, .76, "plaster"),
        ("Shop across the north lane", 8.09, -12.52, "front", 4.23, 4.15, 4.78, .72, "tile"),
        ("East lane repair shop", 14.39, -6.96, "front", 4.52, 4.35, 4.67, .63, "cedar"),
    ]
    for label, x, z, facing, width, depth, eave, rise, material in houses:
        front = Facade(kit, f"Konbini/{label}", x, .122, z, facing)
        _japanese_house(front, width, depth, eave, rise, material, m, shopfronts.get(label))

    cedar_return = Facade(kit, "Konbini/Cedar house road-facing gable", -8.375, .122, 2.015)
    _japanese_return(cedar_return, 3.45, m | {"mahogany": persimmon, "warmWindow": rose_window, "porch": apricot}, "cedar")
    tile_return = Facade(kit, "Konbini/Tile shop outer gable", 1.78, .122, -8.385, "right")
    _japanese_return(tile_return, 3.45, m, "tile")

    workshop = Facade(kit, "Konbini/Side-street workshop", 6.78, .122, -3.50, "left")
    workshop.span("Single storey workshop", -1.55, 1.55, 0, 2.05, -2.25, 0, m["tile"], .034)
    _roof(workshop, 3.10, 2.25, 2.14, .37, m["roofBlue"], m["gutter"], "saltbox")
    workshop.box("Recessed loading shutter", .13, 1.06, .033, 1.91, 1.87, .09, m["gutter"], .023)
    for slat in range(13):
        workshop.box("Shutter fold", .13, .20 + slat * .132, .089, 1.83, .034, .036, m["ac"])
    _window(workshop, -1.11, 1.33, .43, .64, m["wood"], m["plinth"], m["glass"], divisions=1)
    workshop_return = Facade(kit, "Konbini/Workshop street gable", 7.91, .122, -1.95)
    _window(workshop_return, -.22, 1.39, 1.20, .76, m["wood"], m["plinth"], blue_window, m["curtains"])
    _side_lintel(workshop_return, -.21, 1.91, 1.56, jade, m["gutter"], mint, mint)
    workshop_return.box("Enamel skirting panel", 0, .51, .052, 2.25, .66, .104, jade, .017)

    # Low parking furniture gives the opposite fronts a silhouette while the
    # existing crosswalks, sidewalks, and turning lanes remain open.
    for index in range(4):
        x = -4.95 + 1.84 * index
        _surface(kit, "Konbini/Small parking stall", x - .035, x + .035, 6.86, 9.2, .126, paint)
        kit.box("Konbini/Parking wheel stop", (x + .86, .18, 9.12), (.86, .12, .19), curb, .026)
    foliage = _foliage(kit, ("#2e4830", "#4c6336", "#647c44"))
    _planter(kit, "Konbini/Corner planting", 7.65, 6.59, 2.6, .63, .122, curb, m["soil"], foliage, 3)
    _planter(kit, "Konbini/Narrow sidewalk planting", 6.67, -.26, .62, 1.35, .122, curb, m["soil"], foliage, 4)
    for x, z in [(-4.43, 6.45), (-3.24, 6.45), (6.29, 7.21), (6.29, 8.42)]:
        _bollard(kit, "Konbini/Parking bollard", x, .122, z, m["ac"], m["gutter"])
    _japanese_annex(Facade(kit, "Konbini/Opposite dry cleaner", -7.78, .122, 8.10, "right"), 3.65, 3.02,
                    m | {"edge": denim, "linen": denim, "porch": mint, "warmWindow": blue_window}, tinted=True, fascia=mint)
    _japanese_annex(Facade(kit, "Konbini/Corner bicycle workshop", 9.01, .122, 6.78), 3.22, 1.14,
                    m | {"edge": persimmon, "linen": persimmon, "porch": apricot, "warmWindow": rose_window}, fascia=apricot)
    _parking_pocket(kit, "Konbini/Roadside bicycle and car parking", 6.73, 11.86, -.53, 1.89, .128, 3,
                    m["plinth"], curb, paint)
    _japanese_annex(Facade(kit, "Konbini/Shop behind the parking court", -3.68, .122, 10.91, "rear"),
                    4.12, 3.59, m, tinted=True)
    _japanese_annex(Facade(kit, "Konbini/Neighborhood delivery shop", 10.48, .122, 10.76, "rear"),
                    4.61, 3.81, m)
    _outer_blocks(kit, "Konbini", .122, 2.50, (2, 2, 3, 2),
                  (m["plaster"], m["tile"], m["cedar"]), m["gutter"], m["roofBlue"],
                  m["glass"], m["curtains"], (m["warmWindow"], blue_window), "gable")


def _window_cage(front, u, y, width, height, metal):
    depth = .28
    for sign in (-1, 1):
        edge = u + sign * width / 2
        front.box("Projection cage side", edge, y, depth / 2, .025, height, depth, metal)
        front.box("Projection cage crossbar", u, y + sign * height * .48, depth, width, .027, .029, metal)
    for index in range(6):
        x = u + (index / 5 - .5) * width
        front.box("Projection cage vertical bar", x, y, depth, .018, height, .018, metal)
    front.box("Projection cage lower shelf", u, y - height / 2, depth / 2, width, .02, depth, metal)


def _kowloon_block(front, width, depth, floors, design, index, materials, shop=None):
    m = materials
    ground_height, storey = KOWLOON_SHOP_HEIGHT, KOWLOON_STOREY_HEIGHT
    y = ground_height + floors * storey
    front.span("Masonry core", -width / 2, width / 2, -.025, y, -depth, 0, m[design], .034)
    front.box("Base damp course", 0, .25, .024, width, .46, .11, m["stain"], .010)
    front.box("Slab edge over shop", 0, ground_height, .057, width + .07, .17, .26, m["slab"], .019)
    if shop:
        paint, lamp, pane = shop
        front.box("Glazed shop fascia", 0, ground_height + .26, .084, width * .92, .31, .17, paint, .013)
        front.panel("Fascia diffuser", 0, ground_height + .25, .171, width * .71, .065, lamp)
    if index % 3 != 0:
        _door(front, -width * .28, .105, .68, 1.92, m["iron"], m["shutter"], m["dark"])
        shutter_width = width * .51
        u = width * .19
        front.box("Recessed rolling shutter", u, 1.11, .043, shutter_width, 1.99, .095, m["iron"], .020)
        for slat in range(16):
            front.box("Rolling shutter slat", u, .22 + slat * .115, .109, shutter_width - .085, .043, .045,
                      paint if shop else m["shutter"])
        front.box("Folded metal awning", u, 2.18, .25, shutter_width + .24, .095, .53, m["canopy"], .012)
        if index % 3 == 1:
            front.box("Magenta shutter awning tube", u, 2.116, .49, shutter_width * .87, .028, .034,
                      m["pinkTube"])
    else:
        _door(front, width * .31, .11, .68, 1.93, m["iron"], m["shutter"], m["dark"])
        _window(front, -width * .16, 1.18, width * .54, 1.50, m["iron"], m["slab"], m["shop"], divisions=3)
        if shop:
            front.box("Glazed window kickplate", -width * .16, .265, .072, width * .54, .26, .14, paint, .016)
        front.box("Awning folded seam", -width * .14, 2.15, .30, width * .78, .075, .58, m["canopy"], .014)
        front.ceiling_light(-width * .16, 2.098, .35, .40, .08, m["iron"], m["tube"])
    for level in range(floors):
        floor = ground_height + level * storey
        front.box("Exposed slab end", 0, floor + .061, .040, width + .025, .10, .14, m["slab"], .008)
        for bay in range(2):
            u = (bay - .5) * width * .52
            seed = index * 11 + level * 7 + bay
            glass = pane if shop and level == 0 and bay == 1 else m["window" if seed % 7 in (0, 1) else "dark"]
            _window(front, u, floor + 1.28, width * .32, .92, m["iron"], m["slab"],
                    glass, m["cloth"] if seed % 5 == 2 else None)
            if seed % 4 in (0, 1):
                _window_cage(front, u, floor + 1.26, width * .36, 1.03, m["cage"])
            if seed % 5 == 3:
                _air_conditioner(front, u, floor + .245, .23, m["casing"], m["iron"])
            if seed % 4 == 2:
                # Alternating fixed repair panels break up the original facade bays.
                front.box("Irregular facade repair", u + .12, floor + .19, .027, width * .28, .14, .033, m["repair"])
    for i in range(2):
        u = width * (.445 - i * .055)
        front.rod("Cast iron rain pipe", (u, .12, .068), (u, y + .16, .068), .038 - i * .006, m["pipe"])
        for level in range(floors + 1):
            front.box("Pipe fixing strap", u, .53 + level * storey, .068, .094, .027, .095, m["cage"])
    _flat_roof(front, width, depth, y, m["slab"], m["tar"])
    if index % 3 == 1:
        front.span("Roof stair headhouse", -.47, .51, y + .06, y + 1.1, -depth + .15, -depth + 1.20, m["repair"], .022)
        front.box("Head house flashing", .02, y + 1.10, -depth + .67, 1.12, .083, 1.18, m["canopy"], .013)
    else:
        u, d = width * .21, -depth * .64
        front.box("Tank support frame", u, y + .17, d, .84, .24, .9, m["iron"], .020)
        front.rod("Rooftop water tank", (u, y + .30, d), (u, y + 1.07, d), .39, m["tank"], sides=16)
        for h in (.36, .67, .99):
            front.rod("Tank stiffening band", (u, y + h, d), (u, y + h + .045, d), .402, m["cage"], sides=16)
        front.rod("Tank feed pipe", (u + .38, y + .6, d), (u + .6, y + .6, d), .032, m["pipe"])
        front.rod("Tank outlet pipe", (u + .6, y + .6, d), (u + .6, y + .1, d), .032, m["pipe"])
    if index % 2 == 0:
        u, d = -width * .28, -depth * .35
        front.rod("TV antenna mast", (u, y + .11, d), (u, y + 1.25, d), .019, m["iron"])
        front.rod("TV antenna boom", (u - .29, y + 1.10, d), (u + .29, y + 1.10, d), .013, m["iron"])
        for bar in range(4):
            u_bar = u - .23 + bar * .15
            length = .37 - bar * .045
            front.rod("TV aerial dipole", (u_bar, y + 1.10, d - length), (u_bar, y + 1.10, d + length),
                      .009, m["iron"], sides=6)


def _tenement_return(front, width, floors, m):
    height = KOWLOON_SHOP_HEIGHT + floors * KOWLOON_STOREY_HEIGHT
    for sign in (-1, 1):
        front.box("Raised corner pilaster", sign * (width / 2 - .075), height / 2, .052,
                  .15, height - .12, .14, m["repair"], .012)
    _window(front, -.26, 1.21, width * .52, 1.34, m["iron"], m["slab"], m["shop"], divisions=3)
    _side_lintel(front, -.26, 2.11, width * .66, m["canopy"], m["iron"], m["tube"])
    for i in range(floors):
        y = KOWLOON_SHOP_HEIGHT + i * KOWLOON_STOREY_HEIGHT
        front.box("Cast slab return", 0, y + .062, .044, width, .11, .15, m["slab"], .009)
        _window(front, -width * .20, y + 1.28, width * .32, .92, m["iron"], m["slab"],
                m["window"] if i % 2 == 0 else m["dark"], m["cloth"])
        _window(front, width * .24, y + 1.34, width * .24, 1.02, m["iron"], m["slab"],
                m["dark"] if i % 3 == 0 else m["window"], divisions=1)
        if i % 2 == 0:
            _window_cage(front, -width * .20, y + 1.26, width * .38, 1.03, m["cage"])
        else:
            _air_conditioner(front, -width * .20, y + .27, .20, m["casing"], m["iron"])
    front.rod("Cast iron stack", (width * .423, .12, .086), (width * .423, height + .02, .086),
              .041, m["pipe"])


def _alley_shop(front, width, depth, m, enamel, lens, pane):
    front.span("Street shop reinforced masonry", -width / 2, width / 2, 0, 2.39, -depth, 0, m["dust"], .027)
    front.box("Raised entry slab", 0, .09, .23, width, .16, .47, m["slab"], .019)
    front.box("Enamel awning", 0, 2.30, .27, width + .12, .13, .59, enamel, .013)
    front.box("Metal fascia", 0, 2.18, .59, width + .13, .15, .085, enamel, .012)
    _door(front, width * .32, .16, .67, 1.89, m["iron"], enamel, m["dark"])
    _window(front, -width * .19, 1.23, width * .47, 1.33, m["iron"], m["slab"], pane, divisions=3)
    front.ceiling_light(-width * .17, 2.232, .35, .45, .095, m["iron"], m["tube"])
    front.panel("Shop fascia diffuser", 0, 2.17, .634, width * .84, .055, lens)
    _flat_roof(front, width, depth, 2.44, enamel, m["tar"])


def _sagging_wire(kit, label, start, end, sag, material):
    points = []
    for index in range(8):
        t = index / 7
        x = start[0] + (end[0] - start[0]) * t
        y = start[1] + (end[1] - start[1]) * t - 4 * sag * t * (1 - t)
        z = start[2] + (end[2] - start[2]) * t
        points.append((x, y, z))
    for a, b in zip(points, points[1:]):
        kit.rod(label, a, b, .016, material, sides=6)


def _kowloon(kit, curb, paint):
    m = {
        "dust": kit.mat("Aged grey Kowloon concrete", "#6e675f", .81, kind=15),
        "blue": kit.mat("Faded blue plaster", "#535e64", .81, kind=15),
        "ochre": kit.mat("Sooted glazed masonry", "#7b6153", .74, kind=14),
        "salt": kit.mat("Water streaked rose stucco", "#776967", .86, kind=15),
        "slab": kit.mat("Sooted poured beam", "#504b4b", .88, kind=15),
        "repair": kit.mat("Unpainted patched concrete", "#837c72", .87, kind=15),
        "stain": kit.mat("Damp black mortar", "#403934", .93, kind=14),
        "iron": kit.mat("Dark welded iron fittings", "#34363c", .53, .38),
        "pipe": kit.mat("Oxidised drainage steel", "#5b514b", .72, .26),
        "cage": kit.mat("Dull galvanised angle", "#605e5b", .63, .34),
        "casing": kit.mat("Flaking ivory enamel", "#938c7d", .66, .13),
        "shutter": kit.mat("Flaking faded-green shutter", "#485953", .63, .22),
        "canopy": kit.mat("Creased zinc shop awning", "#475756", .61, .31, kind=16),
        "tar": kit.mat("Patched bituminous roof", "#353337", .93, kind=15),
        "tank": kit.mat("Dulled cylindrical blue tank", "#486472", .47, .14),
        "dark": kit.mat("Dark apartment glass", "#27303a", .27, .09),
        "cloth": kit.mat("Lined apartment voile", "#8b8074", .94),
        "window": kit.mat("Dim amber apartment window", "#655447", .56, emission=(.43, .23, .11)),
        "shop": kit.mat("Old shop fluorescent glazing", "#576961", .49, emission=(.18, .32, .25)),
        "tube": kit.mat("Glazed cyan shop diffuser", "#9cecf2", .42, emission=(1.15, 3.3, 3.6)),
        "pinkTube": kit.mat("Magenta tube beside closed shop", "#ff6ec2", .38, emission=(4.3, .42, 2.5)),
        "cyanTube": kit.mat("Cyan tube under shop fascia", "#64efff", .38, emission=(.42, 3.2, 4.2)),
        "banner": kit.mat("Painted red tin shop banner", "#7a4037", .54, .16),
        "soffit": kit.mat("Cream shop name enamel", "#a09679", .64, .16),
    }
    turquoise = kit.mat("Kowloon/Oxidized turquoise shop enamel", "#128980", .48, .13)
    garnet = kit.mat("Kowloon/Garnet shop enamel", "#b63b68", .46, .13)
    ochre = kit.mat("Kowloon/Golden ochre shop enamel", "#cf8b29", .49, .12)
    chartreuse = kit.mat("Kowloon/Lime fascia diffuser", "#d4fb73", .40, emission=(1.69, 3.70, .48))
    amber = kit.mat("Kowloon/Amber fascia diffuser", "#ffbd57", .40, emission=(4.4, 2.25, .37))
    lavender = kit.mat("Kowloon/Lavender upstairs glazing", "#a68ed5", .53, emission=(.66, .47, 1.12))
    green_window = kit.mat("Kowloon/Green upstairs glazing", "#7bb39d", .51, emission=(.24, .82, .54))
    shopfronts = {
        "Shutter tenement": (garnet, m["cyanTube"], lavender),
        "West balcony tenement": (turquoise, chartreuse, green_window),
        "Front lane walk-up": (ochre, amber, lavender),
        "Pink tank house": (garnet, chartreuse, green_window),
        "Eastern roof service block": (turquoise, amber, lavender),
    }
    # These fronts create narrow passages outside the solid L already in the
    # scene. No occupied buildings extend into the bar or its right-front alley.
    blocks = [
        ("Western lane tower", -6.76, -3.86, "right", 2.74, 3.12, 3, "blue"),
        ("Shutter tenement", -6.83, -.81, "right", 3.05, 2.56, 2, "ochre"),
        ("West balcony tenement", -6.70, 2.13, "right", 2.72, 3.38, 2, "dust"),
        ("Front lane walk-up", -6.93, 5.22, "right", 3.02, 2.46, 2, "salt"),
        ("Pink tank house", -4.74, -6.83, "front", 2.91, 3.24, 2, "salt"),
        ("Repair shop apartments", -1.74, -6.72, "front", 2.71, 2.72, 3, "dust"),
        ("Blue tiled apartments", 1.19, -6.88, "front", 2.92, 3.22, 2, "blue"),
        ("Ochre stairwell block", 4.22, -6.74, "front", 2.91, 2.85, 2, "ochre"),
        ("Eastern roof service block", 7.48, -7.45, "front", 2.84, 2.98, 2, "dust"),
        ("Second-row west corner walk-up", -10.95, 4.98, "front", 2.95, 3.15, 2, "blue"),
        ("Further east loading-court flats", 11.38, -5.06, "front", 3.15, 3.60, 3, "salt"),
        ("West lane corner apartments", -14.54, -8.46, "right", 5.32, 4.48, 3, "ochre"),
        ("West lane market walk-up", -15.26, -2.64, "right", 4.79, 4.67, 3, "salt"),
        ("West lane courtyard tenement", -15.05, 3.68, "right", 5.41, 5.35, 2, "blue"),
        ("Rear courtyard repair arcade", -8.91, -13.25, "front", 5.63, 4.68, 3, "dust"),
        ("Rear row pink tiled apartments", -2.40, -13.78, "front", 5.54, 5.44, 3, "salt"),
        ("Rear row loading offices", 3.79, -13.96, "front", 5.21, 4.79, 2, "ochre"),
        ("North-east municipal flats", 10.36, -13.45, "front", 5.68, 5.10, 3, "blue"),
        ("East loading lane apartments", 16.53, -9.50, "front", 5.74, 4.38, 3, "dust"),
    ]
    for index, (label, x, z, facing, width, depth, floors, material) in enumerate(blocks):
        front = Facade(kit, f"Kowloon/{label}", x, .022, z, facing)
        _kowloon_block(front, width, depth, floors, material, index, m, shopfronts.get(label))
    west_return = Facade(kit, "Kowloon/Front lane gable windows", -8.16, .022, 6.73)
    _tenement_return(west_return, 2.46, blocks[3][6], m)
    east_return = Facade(kit, "Kowloon/Service block return windows", 8.90, .022, -8.94, "right")
    _tenement_return(east_return, 2.98, blocks[8][6], m)
    # Ceramic standoffs carry the wires between rear facades above the passage.
    terminals = []
    for index, (_, x, z, _, _, _, _, _) in enumerate(blocks[4:9]):
        y = 5.38 + .34 * min(index, 4 - index)
        u = x + .26
        pole_end = (u, y, z + .34)
        kit.rod("Kowloon/Utility standoff", (u, y, z), pole_end, .036, m["pipe"], sides=8)
        terminals.append(pole_end)
    for start, end in zip(terminals, terminals[1:]):
        _sagging_wire(kit, "Kowloon/Rear utility cable", start, end, .31, m["iron"])

    annex = Facade(kit, "Kowloon/Side lane service kiosk", 7.02, .022, -4.72, "left")
    annex.span("Single storey kiosk", -1.41, 1.41, 0, 2.14, -1.67, 0, m["stain"], .026)
    _roof(annex, 2.82, 1.67, 2.20, .28, m["canopy"], m["iron"], "saltbox")
    _window(annex, .28, 1.14, 1.46, 1.21, m["iron"], m["slab"], m["shop"], divisions=3)
    _door(annex, -.95, .09, .63, 1.84, m["iron"], m["shutter"])
    annex.box("Older sign face", .30, 1.91, .077, 1.57, .31, .085, m["banner"], .012)
    for step in range(6):
        annex.box("Raised lettering stroke", -.28 + step * .23, 1.90, .128, .086, .093, .017, m["soffit"])
    _air_conditioner(annex, .96, 2.03, .20, m["casing"], m["iron"])

    for i in range(10):
        z = .85 + i * .71
        _surface(kit, "Kowloon/Alley trench rim", 6.18, 6.42, z - .34, z + .34, .031, m["slab"])
        for slot in range(4):
            inset_z = z + (slot - 1.5) * .14
            _surface(kit, "Kowloon/Alley grate slots", 6.24, 6.37, inset_z - .016, inset_z + .016, .032, m["iron"])
    for x, z in [(6.62, 4.75), (6.62, 5.90), (6.62, 7.04)]:
        _bollard(kit, "Kowloon/Delivery lane protection", x, .022, z, paint, m["iron"])
    _alley_shop(Facade(kit, "Kowloon/West frontage tool store", -8.51, .022, 8.40, "right"), 3.1, 2.67,
               m, turquoise, chartreuse, green_window)
    _alley_shop(Facade(kit, "Kowloon/Low delivery-lane shop", 9.6, .022, .86), 3.0, 2.13,
               m, garnet, amber, lavender)
    _parking_pocket(kit, "Kowloon/Small loading court", 7.34, 11.53, 1.83, 5.10, .029, 3,
                    m["stain"], curb, paint)
    _surface(kit, "Kowloon/Shopfront concrete apron", -6.38, 17.65, 9.64, 16.38, .041, m["slab"])
    for label, x, z, width, depth, wall, pane in [
        ("Metal merchant", -3.62, 10.72, 4.48, 3.18, "blue", m["shop"]),
        ("Shuttered market depot", 2.32, 11.80, 4.66, 3.77, "ochre", m["window"]),
        ("Alley machine shop", 9.02, 10.55, 4.84, 3.86, "dust", m["shop"]),
        ("Market storage and deliveries", 14.64, 11.46, 4.79, 4.19, "salt", m["window"]),
    ]:
        workshop = Facade(kit, f"Kowloon/Opposite {label}", x, .041, z, "rear")
        _service_shed(workshop, width, depth, m[wall], m["iron"], m["slab"], m["shutter"], m["dark"],
                      pane, m["tube"])
        _flat_roof(workshop, width, depth, 2.46, m["slab"], m["tar"])
    _outer_blocks(kit, "Kowloon", .022, 2.52, (4, 6, 5, 4, 7),
                  (m["dust"], m["blue"], m["ochre"], m["salt"]), m["slab"], m["tar"],
                  m["dark"], m["cloth"], (m["window"], m["shop"]), "flat")


def _motel_stair(front, u, y, base_depth, concrete, rail):
    count, width, going = 13, 1.04, .212
    rear = base_depth
    for step in range(count):
        height = y * (count - step) / count
        d = rear + (step + .5) * going
        front.box("Concrete stair tread", u, height - .074, d, width, .148, going + .022, concrete, .009)
        if step % 3 == 0:
            for side in (-1, 1):
                front.rod("Stair railing post", (u + side * .49, height, d),
                          (u + side * .49, height + .83, d), .023, rail)
    for side in (-1, 1):
        x = u + side * .49
        front.rod("Continuous stair handrail", (x, y + .855, rear),
                  (x, .93, rear + going * count), .032, rail)
        front.rod("Stair stringer", (x, y - .14, rear),
                  (x, .034, rear + going * count), .063, rail)


def _motel_wing(front, width, depth, rooms, floors, m, stair=None, fascia=None):
    floor_pitch = 2.29
    ceiling = floor_pitch * floors + .035
    front.span("Painted guest wing", -width / 2, width / 2, 0, ceiling, -depth, 0, m["stucco"], .026)
    front.span("Walkway footing", -width / 2 - .12, width / 2 + .12, 0, .11, -.08, 1.13, m["walk"], .019)
    for storey in range(floors):
        y = storey * floor_pitch
        if storey:
            _balcony(front, width + .19, y, 1.13, m["walk"], m["rail"], posts=rooms * 8, stair=stair)
        else:
            front.box("Painted baseboard", 0, .18, .014, width, .19, .09, m["brick"])
        for room in range(rooms):
            bay_width = width / rooms
            u = (room + .5) * bay_width - width / 2
            front.box("Party wall pier", u - bay_width / 2 + .025, y + 1.12, .044, .112, 2.22, .153, m["brick"], .014)
            door = m["teal"] if room % 2 == 0 else m["cedar"]
            _door(front, u - bay_width * .25, y + .11, .67, 1.91, m["rail"], door)
            pane = m["dark"]
            if (room + storey) % 3 == 0:
                pane = m["window"]
            elif fascia and storey and (room + storey) % 4 == 2:
                pane = m["coolWindow"]
            _window(front, u + bay_width * .16, y + 1.26, bay_width * .40, .86, m["rail"], m["walk"], pane, m["curtains"])
            if (room + storey) % 2 == 0:
                _air_conditioner(front, u + bay_width * .16, y + .54, .175, m["casing"], m["rail"])
            if (room + storey) % 3 != 1:
                diffuser = y + (2.128 if storey < floors - 1 else 2.26)
                front.ceiling_light(u - bay_width * .23, diffuser, .58, .21, .11, m["rail"], m["porch"])
            front.box("Room number plate", u - bay_width * .25, y + 1.71, .118, .16, .092, .022, m["aluminum"], .008)
    front.box("Roof over the arcade", 0, ceiling + .024, .54, width + .26, .16, 1.19, m["standing"], .021)
    front.box("Colored fascia band", 0, ceiling + .025, 1.12, width + .28, .20 if fascia else .12,
              .116, m["teal"], .018)
    if fascia:
        for side in (-1, 1):
            front.panel("Motel arcade fascia lens", side * width * .28, ceiling + .024, 1.180,
                        width * .30, .042, fascia)
    if stair is not None:
        _motel_stair(front, stair, floor_pitch, 1.03, m["walk"], m["rail"])
    if floors == 2:
        _flat_roof(front, width, depth, ceiling + .11, m["walk"], m["standing"], returns=False)
        for sign in (-1, 1):
            u = sign * width * .27
            front.box("Vented rooftop service riser", u, ceiling + .38, -depth * .66, .64, .48, .59, m["walk"], .021)
            front.box("Riser rain cowl", u, ceiling + .65, -depth * .66, .82, .079, .79, m["aluminum"], .016)
    else:
        _roof(front, width, depth, ceiling + .15, .63, m["standing"], m["rail"], "hip")


def _service_return(front, width, m, bright=False, fascia=None):
    front.box("Painted brick plinth", 0, .41, .045, width, .49, .13, m["brick"], .014)
    for sign in (-1, 1):
        front.box("Masonry corner board", sign * (width / 2 - .064), 1.36, .027, .14, 2.11, .10, m["walk"], .010)
    _window(front, -.09, 1.43, width * .60, .92, m["rail"], m["walk"],
            m["window"] if bright else m["dark"], m["curtains"] if bright else None, divisions=3)
    _side_lintel(front, -.09, 2.078, width * .78, m["teal"], m["rail"], m["porch"], fascia)


def _service_shed(front, width, depth, wall, frame, plinth, shutter, glass, pane, work_light):
    front.span("Workshop walls", -width / 2, width / 2, 0, 2.46, -depth, 0, wall, .027)
    front.box("Loading step", 0, .062, .24, width, .124, .50, plinth, .018)
    u = -width * .18
    front.box("Recessed rolling door", u, 1.11, .030, width * .56, 2, .088, frame, .019)
    for slat in range(14):
        front.box("Door slat", u, .24 + .13 * slat, .087, width * .53, .048, .037, shutter)
    front.box("Entry lintel", u, 2.18, .182, width * .64, .12, .41, frame, .012)
    front.ceiling_light(u, 2.065, .275, .43, .079, frame, work_light)
    _door(front, width * .32, .12, .66, 1.87, frame, shutter, glass)
    back = Facade(front.kit, front.name + " rear wall", *front.point(0, 0, -depth))
    _window(back, -.24, 1.57, width * .36, .62, frame, plinth, pane, divisions=3)
    side = Facade(front.kit, front.name + " service return",
                  *front.point(-width / 2, 0, -depth / 2), "right")
    _window(side, .24, 1.56, 1.08, .48, frame, plinth, pane)


def _station(kit, curb, paint):
    m = {
        "stucco": kit.mat("Warm motel pebble-dash", "#90877c", .84, kind=15),
        "brick": kit.mat("Motel brick piers", "#674d3f", .82, kind=14),
        "walk": kit.mat("Painted corridor concrete", "#727580", .77, kind=15),
        "rail": kit.mat("Blackened tubular motel railing", "#3e464c", .52, .29),
        "standing": kit.mat("Weathered standing-seam motel roof", "#4b5457", .50, .32, kind=16),
        "aluminum": kit.mat("Dull spun aluminum", "#8d8e88", .49, .46),
        "teal": kit.mat("Petrol green motel enamel", "#406461", .62),
        "cedar": kit.mat("Muted maroon motel door", "#62443e", .65),
        "dark": kit.mat("Dark motel glazing", "#293740", .29, .11),
        "curtains": kit.mat("Motel drapery", "#999789", .94),
        "casing": kit.mat("Ivory motel air conditioner enamel", "#95968a", .63, .10),
        "window": kit.mat("Occupied motel room window", "#78644f", .52, emission=(.40, .25, .14)),
        "coolWindow": kit.mat("Station/Blue motel room window", "#6388a1", .52, emission=(.25, .59, 1.02)),
        "porch": kit.mat("Motel warm corridor diffuser", "#bcac8b", .41, emission=(3.1, 2.17, 1.16)),
        "office": kit.mat("Pale office ceiling diffuser", "#b5bcb0", .39, emission=(2.15, 2.61, 2.40)),
        "officeNeon": kit.mat("Coral reception fascia tube", "#ff6c88", .37, emission=(5.0, .68, 1.35)),
        "soil": kit.mat("Shoulder soil", "#34312a", .96),
    }
    teal = kit.mat("Station/Turquoise motel enamel", "#198e88", .43, .18)
    coral = kit.mat("Station/Retro coral fascia enamel", "#c65843", .46, .17)
    navy = kit.mat("Station/Marine blue workshop roofing", "#286787", .49, .30, kind=16)
    honey = kit.mat("Station/Honey motel fascia lens", "#ffc55a", .39, emission=(4.15, 2.32, .30))
    aqua = kit.mat("Station/Aqua workshop fascia lens", "#68e9f5", .39, emission=(.39, 2.52, 3.26))
    pink_window = kit.mat("Station/Warm pink reception window", "#aa737f", .52, emission=(1.06, .47, .48))
    main = Facade(kit, "Station/Two-storey motel", -4.45, .021, -7.38)
    _motel_wing(main, 12.8, 3.05, 6, 2, m | {"teal": teal}, stair=-5.66, fascia=honey)
    wing = Facade(kit, "Station/Setback motel wing", 5.64, .021, -8.05)
    _motel_wing(wing, 6.85, 3.15, 3, 1, m | {"teal": coral, "standing": navy}, fascia=aqua)

    office = Facade(kit, "Station/Roadside reception", -7.52, .021, -1.18, "right")
    office.span("Reception masonry", -1.69, 1.69, 0, 2.39, -2.10, 0, m["brick"], .033)
    office.box("Porch step", 0, .071, .32, 3.36, .142, .68, m["walk"], .020)
    office.box("Reception overhang", 0, 2.35, .48, 3.49, .139, .96, m["walk"], .019)
    office.box("Enamel reception fascia", 0, 2.37, .98, 3.49, .23, .12, coral, .018)
    office.box("Reception canopy light strip", 0, 2.234, .95, 3.27, .029, .05, m["officeNeon"])
    _door(office, 1.0, .142, .74, 1.96, m["rail"], m["teal"], m["dark"])
    _window(office, -.49, 1.27, 1.58, 1.24, m["rail"], m["walk"], pink_window, m["curtains"], divisions=3)
    office.ceiling_light(-.09, 2.269, .58, .72, .10, m["rail"], m["office"])
    _roof(office, 3.38, 2.10, 2.47, .47, m["standing"], m["rail"], "saltbox")
    _service_return(Facade(kit, "Station/Reception window return", -8.57, .021, .51), 2.10,
                    m | {"teal": coral, "window": pink_window}, bright=True, fascia=honey)

    garage = Facade(kit, "Station/Tyre service garage", 7.87, .021, -3.68, "left")
    garage.span("Concrete service bay", -2.05, 2.05, 0, 2.34, -2.42, 0, m["walk"], .032)
    garage.box("Band above garage door", 0, 2.19, .078, 4.12, .23, .19, coral, .019)
    garage.box("Inset rolling door", -.39, 1.12, .041, 2.50, 1.98, .10, m["rail"], .016)
    for slat in range(16):
        garage.box("Garage shutter fold", -.39, .17 + slat * .123, .103, 2.41, .043, .039, m["aluminum"])
    _door(garage, 1.37, .06, .64, 1.90, m["rail"], m["teal"])
    _roof(garage, 4.10, 2.42, 2.46, .38, navy, m["rail"], "saltbox")
    _service_return(Facade(kit, "Station/Garage side office", 9.08, .021, -1.63), 2.42,
                    m | {"teal": teal, "window": m["coolWindow"]}, bright=True, fascia=aqua)
    _service_return(Facade(kit, "Station/Motel east room return", 9.065, .021, -9.625, "right"), 3.15, m, bright=True)

    laundry = Facade(kit, "Station/Roadside motel laundry", -11.49, .021, -3.64)
    _motel_wing(laundry, 3.46, 2.39, 2, 1, m)
    _service_return(Facade(kit, "Station/Laundry side window", -9.76, .021, -4.835, "right"), 2.39, m)
    outer = Facade(kit, "Station/Far east service rooms", 10.40, .021, 1.34)
    _motel_wing(outer, 3.50, 2.10, 2, 2, m | {"teal": teal, "standing": navy, "cedar": coral}, fascia=aqua)
    roadside_wings = [
        ("West roadside motor lodge", -16.50, -5.36, "right", 8.91, 4.68, 4, 2),
        ("Workshop west of the highway", -17.98, .42, "right", 3.11, 4.54, 1, 1),
        ("Rear travel lodge", -10.09, -14.21, "front", 8.17, 4.31, 4, 2),
        ("Rear motel courtyard wing", -.75, -14.37, "front", 8.24, 4.89, 4, 2),
        ("East station offices", 9.54, -13.09, "front", 8.52, 4.82, 4, 1),
        ("Interchange maintenance rooms", 16.46, -8.35, "front", 7.74, 4.21, 3, 1),
        ("Rooms by the east lay-by", 18.38, -1.72, "front", 5.52, 3.51, 2, 2),
    ]
    for name, x, z, facing, width, depth, rooms, floors in roadside_wings:
        _motel_wing(Facade(kit, f"Station/{name}", x, .021, z, facing), width, depth, rooms, floors, m)
    _parking_pocket(kit, "Station/West roadside parking pocket", -13.73, -9.42, 7.27, 10.21, .123, 2,
                    m["walk"], curb, paint)
    _parking_pocket(kit, "Station/East lay-by", 8.67, 14.51, 7.20, 10.17, .123, 3,
                    m["walk"], curb, paint)
    _surface(kit, "Station/Opposite service road pavement", -12.2, 18.6, 10.21, 17.5, .037, m["walk"])
    ticket_office = Facade(kit, "Station/Opposite ticket office", -6.01, .037, 11.42, "rear")
    _motel_wing(ticket_office, 4.48, 3.35, 2, 1, m)
    _service_return(Facade(kit, ticket_office.name + " service return",
                           *ticket_office.point(-2.24, 0, -1.675), "right"), 3.35, m, bright=True)
    for name, x, z, width, depth, roof, pane in [
        ("Road maintenance garage", .76, 11.10, 4.88, 3.97, "saltbox", m["window"]),
        ("Roadside electrical workshop", 7.86, 11.34, 5.30, 4.36, "gable", m["coolWindow"]),
        ("Highway delivery depot", 14.86, 12.51, 5.20, 4.27, "hip", m["window"]),
    ]:
        shed = Facade(kit, f"Station/{name}", x, .037, z, "rear")
        _service_shed(shed, width, depth, m["stucco"], m["rail"], m["walk"], m["teal"], m["dark"],
                      pane, m["office"])
        _roof(shed, width, depth, 2.52, .51, m["standing"], m["rail"], roof)

    # Leave the station apron and the through lanes in front of the service court open.
    for x in (6.65, 8.62, 12.58, 14.55, 16.52):
        _surface(kit, "Station/Overflow stall stripes", x - .038, x + .038, -1.20, 1.98, .026, paint)
    for x in (7.56, 13.49, 15.46):
        kit.box("Station/Overflow wheel stop", (x, .096, -1.09), (.93, .15, .19), curb, .024)
    foliage = _foliage(kit, ("#384534", "#52653e", "#6f784e"))
    _planter(kit, "Station/Roadside gravel pocket", -5.76, 7.36, 2.95, .52, .120, curb, m["soil"], foliage, 7)
    _planter(kit, "Station/Rear wing planting", 5.88, -6.51, 1.41, .63, .021, curb, m["soil"], foliage, 1)
    for x, z in [(-6.76, -.97), (-6.76, -2.16), (7.15, -2.23), (7.13, -5.20)]:
        _bollard(kit, "Station/Apron corner bollard", x, .021, z, m["aluminum"], m["rail"])
    _outer_blocks(kit, "Station", .021, 2.62, (2, 1, 3, 2, 2),
                  (m["stucco"], m["brick"], m["walk"]), m["walk"], m["standing"],
                  m["dark"], m["curtains"], (m["window"], m["coolWindow"]), "hip")


def _outer_blocks(kit, district, ground, pitch, storeys, walls, trim, roofing, glass, shades, lit_glass, profile):
    # Two street grids carry the far facades through the left and right margins of
    # the orbit. Leave the original east-west and north-south streets as clear gaps.
    west_streets = (-26, -42, -63, -88)
    north_streets = (-25, -41, -62, -86)
    west_lots = (-64, -53, -41, -28, -16, -5, 10, 22, 34)
    north_lots = (-20, -9, -.9, 9.8, 20, 32, 44, 56, 69, 83)
    window_width, window_height = 1.05, .94
    warm, cool = lit_glass
    for facing, streets, lots in (("right", west_streets, west_lots), ("front", north_streets, north_lots)):
        for row, street in enumerate(streets):
            for lot, along in enumerate(lots):
                seed = row * 7 + lot * 3 + (5 if facing == "right" else 0)
                if district == "Station" and seed % 11 == 1:
                    continue
                x, z = (street, along) if facing == "right" else (along, street)
                name = f"{district}/Outer {facing} block {row + 1}-{lot + 1}"
                front = Facade(kit, name, x, ground, z, facing)
                width = 6.15 + .40 * (seed % 7)
                if facing == "front" and lot == 2:
                    width = min(width, 5.7)
                depth = 7.15 + .68 * (seed % 5)
                levels = storeys[seed % len(storeys)]
                height = pitch * levels
                body = walls[(seed // 2 + lot) % len(walls)]
                front.span("Wall envelope", -width / 2, width / 2, -.02, height, -depth, 0, body)
                if profile == "flat" or (district == "Station" and seed % 3 == 0):
                    _flat_roof(front, width, depth, height, trim, roofing)
                    if levels >= 4:
                        front.span("Setback roof headhouse", -.82, 1.13, height, height + 1.48, -depth + .65,
                                   -depth + 2.85, body)
                else:
                    _roof(front, width, depth, height, .80 + .14 * (seed % 5), roofing, trim, profile)
                # The exposed return faces the other approach to the main set.
                end = front.point(width / 2 if facing == "front" else -width / 2, 0, -depth / 2)
                side = Facade(kit, name + " return", *end, "right" if facing == "front" else "front")
                for face, span in ((front, width), (side, depth)):
                    # Each exposed facade gets an occupied window even on a single storey.
                    rooms = Random(face.name)
                    occupied = rooms.sample(range(3 * levels), rooms.randint(1, (levels + 1) // 2))
                    lit = {room: cool if rooms.randrange(5) == 0 else warm for room in occupied}
                    for level in range(levels):
                        floor = pitch * level
                        face.box("Slab band", 0, floor + .12, .035, span, .16, .10, trim)
                        for bay in range(3):
                            u = (bay - 1) * span * .30
                            y = floor + pitch * .59
                            face.box("Window reveal", u, y, .026, 1.21, 1.11, .067, trim)
                            pane = lit.get(3 * level + bay)
                            if pane is None:
                                pane = shades if (seed + 3 * level + bay) % 7 in (0, 1) else glass
                                face.panel("Window inset", u, y, .063, window_width, window_height, pane)
                                continue
                            # Expose part of the glass so the far windows take a small share
                            # of the path tracer's area-weighted direct light samples.
                            opening = rooms.uniform(.20, .32)
                            covered = window_width - opening
                            open_side = rooms.choice((-1, 1))
                            face.panel("Window inset", u + open_side * covered / 2, y, .063,
                                       opening, window_height, pane)
                            face.panel("Window curtain", u - open_side * opening / 2, y, .063,
                                       covered, window_height, shades)


def build(scene_id, kit):
    """Add the district matching an upstream Machi scene."""
    district = {"konbini": _konbini, "kowloon": _kowloon, "station": _station}[scene_id]
    district(kit, *_street(kit, scene_id))
