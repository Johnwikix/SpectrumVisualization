"""Static hardware sized against the pinned Folia Major scenes, in Y-up metres."""

from math import cos, pi, sin, sqrt


def _lathe(kit, name, x, z, profile, material, sides=32, capped=True):
    vertices = []
    for y, radius in profile:
        for side in range(sides):
            angle = 2 * pi * side / sides
            vertices.append((x + radius * cos(angle), y, z + radius * sin(angle)))
    faces = []
    for lower in range(0, len(vertices) - sides, sides):
        upper = lower + sides
        for side in range(sides):
            next_side = (side + 1) % sides
            faces.append((lower + side, upper + side, upper + next_side, lower + next_side))
    if capped:
        faces.append(tuple(range(sides)))
        faces.append(tuple(range(len(vertices) - 1, len(vertices) - sides - 1, -1)))
    kit.mesh(name, vertices, faces, material, smooth=True)


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _unit(v):
    length = sqrt(sum(x * x for x in v))
    return tuple(x / length for x in v)


def _pipe(kit, name, path, radius, material, sides=12):
    """Sweep a round tube along a path that bends chiefly in the XY plane."""
    vertices = []
    last = len(path) - 1
    for index, point in enumerate(path):
        before = path[max(0, index - 1)]
        after = path[min(last, index + 1)]
        tangent = _unit(tuple(b - a for a, b in zip(before, after)))
        axis = _unit(_cross(tangent, (0, 0, 1)))
        other = _cross(tangent, axis)
        for side in range(sides):
            angle = 2 * pi * side / sides
            vertices.append(tuple(p + radius * (u * cos(angle) + v * sin(angle))
                                  for p, u, v in zip(point, axis, other)))
    faces = []
    for ring in range(last):
        lower = ring * sides
        upper = lower + sides
        for side in range(sides):
            next_side = (side + 1) % sides
            faces.append((lower + side, lower + next_side, upper + next_side, upper + side))
    faces.append(tuple(range(sides - 1, -1, -1)))
    faces.append(tuple(range(last * sides, len(vertices))))
    kit.mesh(name, vertices, faces, material, smooth=True)


def _bezier(start, control_a, control_b, end, steps):
    points = []
    for step in range(1, steps + 1):
        t = step / steps
        s = 1 - t
        points.append(tuple(s**3 * a + 3 * s**2 * t * b + 3 * s * t**2 * c + t**3 * d
                            for a, b, c, d in zip(start, control_a, control_b, end)))
    return points


def _downlight(kit, name, center, size, bezel, reflector, lens, backing=True):
    x, y, z = center
    width, depth = size
    rim = .022
    if backing:
        kit.box(f"{name}/Backplate", (x, y + .024, z), (width + 2 * rim, .024, depth + 2 * rim),
                reflector, bevel=.004)
        kit.mesh(f"{name}/Diffuser",
                 [(x - width / 2, y, z - depth / 2), (x + width / 2, y, z - depth / 2),
                  (x + width / 2, y, z + depth / 2), (x - width / 2, y, z + depth / 2)],
                 [(0, 1, 2, 3)], lens)
    for side in (-1, 1):
        kit.box(f"{name}/Long rim", (x, y + .002, z + side * (depth + rim) / 2),
                (width + 2 * rim, .032, rim), bezel, bevel=.003)
        kit.box(f"{name}/End rim", (x + side * (width + rim) / 2, y + .002, z),
                (rim, .032, depth), bezel, bevel=.003)
        for end in (-1, 1):
            offset = end * (width / 2 + .010)
            kit.rod(f"{name}/Retainer", (x + offset, y - .017, z + side * (depth / 2 - .010)),
                    (x + offset, y - .013, z + side * (depth / 2 - .010)), .006, reflector, sides=8)


def _awning(kit, name, runs, world, dimensions, shell, edge, steel):
    # A continuous standing fold forms both the outer drip lip and its tucked return.
    section = [
        (2.586, -.006), (2.586, .055), (2.529, .499), (2.516, .548), (2.497, .560),
        (2.453, .560), (2.446, .539), (2.450, .515), (2.466, .518), (2.469, .543),
        (2.495, .541), (2.513, .492), (2.571, .052), (2.571, -.006),
    ]
    if sum(y * zz - yy * z for (y, z), (yy, zz) in zip(section, section[1:] + section[:1])) < 0:
        section.reverse()
    size = len(section)
    for number, (start, end) in enumerate(zip(runs, runs[1:])):
        vertices = [world(u, y, depth) for u in (start + .002, end - .002) for y, depth in section]
        faces = [tuple(reversed(range(size))), tuple(range(size, 2 * size))]
        for index in range(size):
            next_index = (index + 1) % size
            faces.append((index, next_index, size + next_index, size + index))
        kit.mesh(f"{name}/Folded sheet {number + 1}", vertices, faces, shell)
        for depth in (.122, .387):
            kit.box(f"{name}/Sheet clip", world(start + .028, 2.572 - .13 * depth, depth),
                    dimensions(.045, .026, .082), edge, bevel=.005)
    for u in runs:
        kit.box(f"{name}/Fascia splice", world(u, 2.475, .562), dimensions(.060, .065, .016),
                edge, bevel=.004)
        for y in (2.457, 2.492):
            kit.rod(f"{name}/Rivet", world(u, y, .568), world(u, y, .575), .009, steel, sides=10)
    for start, end in zip(runs, runs[1:]):
        kit.box(f"{name}/Underside channel", world((start + end) / 2, 2.527, .215),
                dimensions(end - start, .040, .082), edge, bevel=.006)


def _konbini(kit):
    shell = kit.mat("Konbini/Baked blue-grey sheet", "#465565", .32, .58)
    edge = kit.mat("Konbini/Trim and channel", "#344048", .40, .54)
    silver = kit.mat("Konbini/Galvanized hardware", "#9ba7ac", .31, .83)
    drain = kit.mat("Konbini/Gutter finish", "#647277", .37, .72)
    duct = kit.mat("Konbini/Weatherproof enclosure", "#b4b4a7", .48, .34)
    dark = kit.mat("Konbini/Recessed vent", "#263038", .55, .21)
    lens = kit.mat("Konbini/Warm entry diffuser", "#fff0da", .42, emission=(12.0, 9.3, 6.6))
    cool_lens = kit.mat("Konbini/Cyan service diffuser", "#77ebff", .43, emission=(2.8, 11.5, 15.4))
    pink_strip = kit.mat("Konbini/Coral rain hood strip", "#ff5d99", .36, emission=(6.0, .55, 1.9))
    cyan_strip = kit.mat("Konbini/Cyan rain hood strip", "#55e1ff", .36, emission=(.45, 4.6, 6.2))

    def front(u, y, depth):
        return (u, y, .4 + depth)

    def right(u, y, depth):
        return (.8 + depth, y, -u)

    _awning(kit, "Konbini/Front rain hood", (-5.34, -4.56, -3.53, -2.46, -1.39, -.36, .57, 1.36),
            front, lambda u, y, d: (u, y, d), shell, edge, silver)
    _awning(kit, "Konbini/Return rain hood", (-.395, .52, 1.48, 2.62, 3.53, 4.57),
            right, lambda u, y, d: (d, y, u), shell, edge, silver)
    kit.box("Konbini/Front hood illuminated edge", front(-1.99, 2.435, .538), (6.52, .025, .035),
            pink_strip)
    kit.box("Konbini/Return hood illuminated edge", right(2.085, 2.435, .538), (.035, .025, 4.78),
            cyan_strip)

    for x in (-5.04, -3.52, -2.45, -1.37, .72):
        kit.box("Konbini/Canopy wall bracket", (x, 2.45, .415), (.096, .24, .064), edge, bevel=.012)
        kit.rod("Konbini/Canopy diagonal brace", (x, 2.36, .443), (x, 2.515, .788), .023, edge, sides=10)
        for y in (2.374, 2.498):
            kit.rod("Konbini/Bracket anchor", (x, y, .445), (x, y, .455), .014, silver, sides=8)
    for z in (-4.53, -3.51, -2.60, -1.16, .29):
        kit.box("Konbini/Return hood bracket", (.804, 2.45, z), (.064, .24, .096), edge, bevel=.012)
        kit.rod("Konbini/Return diagonal brace", (.831, 2.36, z), (1.167, 2.515, z),
                .023, edge, sides=10)
        for y in (2.374, 2.498):
            kit.rod("Konbini/Return bracket anchor", (.836, y, z), (.846, y, z), .014, silver, sides=8)

    for x in (-3.45, -1.97, -.34):
        kit.box("Konbini/Lamp stand-off", (x, 2.513, .73), (.96, .071, .088), edge, bevel=.008)
        _downlight(kit, "Konbini/Entry luminaire", (x, 2.456, .73), (.97, .062), edge, silver, lens)
    for z in (-1.43, -3.39):
        kit.box("Konbini/Service lamp saddle", (1.11, 2.52, z), (.075, .065, .87), edge, bevel=.008)
        _downlight(kit, "Konbini/Service luminaire", (1.11, 2.462, z), (.074, .86), edge, silver, cool_lens)

    # Collect the return's runoff at the solid jamb beyond the glass and ahead of the vending bay.
    x, z = .913, -2.64
    start = (1.303, 2.49, z)
    vertical = (x, 2.16, z)
    bottom = (x, .31, z)
    path = ([start] + _bezier(start, (.99, 2.49, z), (x, 2.39, z), vertical, 12)
            + [bottom] + _bezier(bottom, (x, .21, z), (1.03, .16, z), (1.16, .16, z), 10))
    _pipe(kit, "Konbini/Formed rainwater drop", path, .037, drain, sides=18)
    kit.box("Konbini/Sump head", (1.294, 2.473, z), (.14, .158, .163), drain, bevel=.019)
    for y in (.47, 1.17, 1.89):
        kit.box("Konbini/Downpipe anchor plate", (.818, y, z), (.05, .112, .127), edge, bevel=.011)
        kit.box("Konbini/Downpipe saddle", (.865, y, z), (.083, .03, .083), drain, bevel=.006)
        _lathe(kit, "Konbini/Pipe collar", x, z, [(y - .016, .04), (y - .011, .049),
               (y + .011, .049), (y + .016, .04)], silver, sides=20, capped=False)
        for side in (-1, 1):
            kit.rod("Konbini/Saddle rivet", (.84, y, z + side * .051),
                    (.853, y, z + side * .051), .011, silver, sides=8)

    # The shallow exhaust grille and its cable box occupy the solid wall above the machines.
    for z in (-3.31, -4.32):
        kit.box("Konbini/Compressor sheet cover", (.846, 2.134, z), (.15, .302, .81), duct, bevel=.018)
        kit.box("Konbini/Exhaust recess", (.925, 2.13, z), (.018, .213, .716), dark, bevel=.007)
        for index in range(8):
            y = 2.042 + index * .025
            kit.box("Konbini/Stamped exhaust louver", (.948, y, z), (.045, .016, .696), duct, bevel=.005)
        for side in (-1, 1):
            for height in (-1, 1):
                kit.rod("Konbini/Cover screw", (.921, 2.134 + height * .128, z + side * .376),
                        (.930, 2.134 + height * .128, z + side * .376), .010, silver, sides=8)
    kit.rod("Konbini/Surface cable conduit", (.858, 2.322, -4.76), (.858, 2.322, -2.87),
            .016, drain, sides=12)
    kit.box("Konbini/Conduit junction", (.862, 2.321, -2.88), (.092, .112, .13), duct, bevel=.012)


# Centers of the seven Tank meshes in the seeded 847bb51d scene. The body
# starts 0.36 m above each roof and the steel shell fits around its 0.33 m radius.
_TANKS = (
    (-4.59048, 6.93, -3.61159),
    (-.95966, 7.03, -5.34068),
    (-.90428, 7.03, -3.44493),
    (-.98458, 7.03, -2.72757),
    (3.17803, 6.23, -5.21920),
    (3.27243, 6.23, -4.34817),
    (2.98204, 6.23, -3.38190),
)


def _kowloon(kit):
    stainless = kit.mat("Kowloon/Cold rolled stainless", "#7f929c", .27, .87)
    bright = kit.mat("Kowloon/Polished welded band", "#a5b3b9", .20, .92)
    frame = kit.mat("Kowloon/Galvanized rack", "#526268", .42, .77)
    rubber = kit.mat("Kowloon/Isolation gasket", "#353139", .64, .13)
    pipe = kit.mat("Kowloon/Weathered brass", "#aa8961", .31, .80)
    red = kit.mat("Kowloon/Valve enamel", "#9b4435", .42, .22)

    for index, (x, roof, z) in enumerate(_TANKS):
        name = f"Kowloon/Water tank {index + 1}"
        bottom = roof + .36
        profile = [(bottom - .022, .321), (bottom - .016, .337), (bottom + .004, .348)]
        for y in (.105, .235, .365, .495):
            profile.extend((bottom + y + offset, radius) for offset, radius in
                           ((-.031, .338), (-.013, .349), (.006, .359), (.020, .348), (.029, .337)))
        profile.extend([(bottom + .590, .338), (bottom + .609, .345), (bottom + .624, .337),
                        (bottom + .652, .309), (bottom + .678, .246), (bottom + .685, .175)])
        _lathe(kit, f"{name}/Rolled and seamed shell", x, z, profile, stainless, sides=32)
        for level in (.033, .563):
            y = bottom + level
            _lathe(kit, f"{name}/Girth band", x, z, [(y - .013, .339), (y - .009, .349),
                   (y + .009, .349), (y + .013, .339)], bright, sides=32, capped=False)
        _lathe(kit, f"{name}/Manway flange", x - .04, z + .025,
               [(bottom + .680, .116), (bottom + .692, .123), (bottom + .702, .123)], rubber, sides=24)
        _lathe(kit, f"{name}/Inspection cover", x - .04, z + .025,
               [(bottom + .700, .118), (bottom + .721, .118),
                (bottom + .733, .107), (bottom + .738, .088)], bright, sides=28)
        for side in (-1, 1):
            hx = x - .04 + side * .045
            kit.rod(f"{name}/Lid handle upright", (hx, bottom + .728, z + .025),
                    (hx, bottom + .767, z + .025), .010, frame, sides=10)
        kit.rod(f"{name}/Lid handle", (x - .085, bottom + .767, z + .025),
                (x + .005, bottom + .767, z + .025), .012, frame, sides=10)
        for dx in (-.22, .22):
            for dz in (-.22, .22):
                kit.box(f"{name}/Leg base plate", (x + dx, roof + .009, z + dz),
                        (.098, .018, .098), frame)
        kit.box(f"{name}/Ladder frame crossbar", (x + .246, roof + .31, z),
                (.058, .053, .496), frame, bevel=.005)
        for side in (-1, 1):
            ladder_z = z + side * .089
            top = roof + .97
            end = (x + .31, roof + 1.035, ladder_z)
            start = (x + .397, top, ladder_z)
            path = [(x + .397, roof + .072, ladder_z), start]
            path += _bezier(start, (x + .397, roof + 1.03, ladder_z),
                            (x + .36, roof + 1.04, ladder_z), end, 6)
            _pipe(kit, f"{name}/Ladder rail", path, .013, frame, sides=10)
        for step in range(5):
            y = roof + .155 + step * .181
            kit.rod(f"{name}/Ladder rung", (x + .397, y, z - .089),
                    (x + .397, y, z + .089), .012, bright, sides=12)
        for y in (roof + .31, roof + .78):
            for side in (-1, 1):
                kit.rod(f"{name}/Ladder stay", (x + .266, y, z + side * .089),
                        (x + .40, y, z + side * .089), .012, frame, sides=10)
        drain_z = z + .188
        bend = (x + .198, roof + .17, drain_z)
        stop = (x + .276, roof + .105, drain_z)
        path = [(x + .198, bottom + .05, drain_z), bend]
        path += _bezier(bend, (x + .198, roof + .12, drain_z),
                        (x + .224, roof + .105, drain_z), stop, 7)
        path.append((x + .49, roof + .105, drain_z))
        _pipe(kit, f"{name}/Drain plumbing", path, .019, pipe, sides=10)
        kit.rod(f"{name}/Valve body", (x + .32, roof + .105, drain_z),
                (x + .399, roof + .105, drain_z), .034, pipe, sides=12)
        kit.rod(f"{name}/Valve stem", (x + .36, roof + .108, drain_z),
                (x + .36, roof + .164, drain_z), .014, frame, sides=10)
        _lathe(kit, f"{name}/Valve wheel", x + .36, drain_z,
               [(roof + .157, .052), (roof + .168, .054), (roof + .173, .043),
                (roof + .164, .039), (roof + .157, .052)], red, sides=18, capped=False)
        kit.box(f"{name}/Valve spoke", (x + .36, roof + .165, drain_z),
                (.087, .016, .013), red, bevel=.003)

    shade = kit.mat("Kowloon/Stall lamp dark enamel", "#2d4a3f", .32, .38)
    white = kit.mat("Kowloon/Stall lamp white enamel", "#c5bca5", .47, .22)
    lens = kit.mat("Kowloon/Stall warm task diffuser", "#fff1d7", .46, emission=(7.2, 4.3, 2.5))
    # The existing 7 cm globe remains the primary emitter below the shade.
    _lathe(kit, "Kowloon/Stall pendant reflector", 5.175, 2.6015,
           [(1.758, .051), (1.749, .075), (1.719, .127), (1.678, .158),
            (1.672, .165), (1.683, .168), (1.727, .138), (1.766, .078),
            (1.784, .054), (1.758, .051)], white, sides=40, capped=False)
    _lathe(kit, "Kowloon/Stall pendant outer shell", 5.175, 2.6015,
           [(1.672, .165), (1.683, .170), (1.727, .140), (1.769, .079),
            (1.79, .049), (1.824, .044), (1.83, .040)], shade, sides=40, capped=False)
    _downlight(kit, "Kowloon/Stall preparation light", (4.73, 2.163, 1.71), (.64, .072), shade, frame, lens)
    for x in (4.48, 4.98):
        kit.rod("Kowloon/Stall lamp hanger", (x, 2.177, 1.71), (x, 2.251, 1.71),
                .014, frame, sides=10)
    _lathe(kit, "Kowloon/Stall stockpot lid", 5.4, 1.775,
           [(1.4, .195), (1.407, .211), (1.420, .210), (1.426, .188),
            (1.45, .132), (1.459, .052)], bright, sides=36)
    kit.box("Kowloon/Stockpot lid grip", (5.4, 1.485, 1.775), (.124, .033, .033),
            rubber, bevel=.012)
    for side in (-1, 1):
        x = 5.4 + side * .232
        kit.box("Kowloon/Stockpot handle mount", (x, 1.297, 1.775),
                (.081, .031, .049), stainless, bevel=.009)
        kit.box("Kowloon/Stockpot handle grip", (x + side * .049, 1.297, 1.775),
                (.092, .038, .105), rubber, bevel=.016)
    for side in (-1, 1):
        z = 1.875 + side * .20
        kit.rod("Kowloon/Wok handle", (4.58, 1.283, z),
                (4.40, 1.30, z + side * .035), .022, frame, sides=14)
    for x in (4.16, 4.46, 4.76, 5.05, 5.35, 5.65, 5.85):
        kit.box("Kowloon/Tarp hem fastener", (x, 1.985, 2.985),
                (.068, .035, .048), frame, bevel=.007)


def _pump(kit, x, z, name, steel, dark, rubber, nozzle, lamp):
    for side in (-1, 1):
        def hose_point(u, y, depth):
            return (x + side * u, y, z + side * depth)

        start = hose_point(.270, 1.554, .074)
        low = hose_point(.468, .483, .209)
        end = hose_point(.358, .888, .197)
        path = [start] + _bezier(start, hose_point(.52, 1.55, .073), hose_point(.57, .493, .215), low, 18)
        path += _bezier(low, hose_point(.338, .474, .207), hose_point(.332, .728, .197), end, 11)
        _pipe(kit, f"{name}/Flexible hose", path, .027, rubber, sides=12)
        kit.rod(f"{name}/Hose coupling", start, hose_point(.317, 1.551, .077), .038, steel, sides=16)
        kit.rod(f"{name}/Nozzle swivel", end, hose_point(.363, .938, .197), .032, steel, sides=16)
        kit.box(f"{name}/Nozzle holster", hose_point(.303, 1.082, .13), (.088, .268, .153), dark, bevel=.025)
        kit.box(f"{name}/Nozzle housing", hose_point(.370, 1.070, .188), (.103, .192, .066), nozzle, bevel=.026)
        kit.rod(f"{name}/Nozzle grip", hose_point(.362, .921, .195), hose_point(.397, 1.099, .193),
                .029, nozzle, sides=14)
        guard_start = hose_point(.395, 1.115, .196)
        guard_end = hose_point(.363, .927, .196)
        guard = [guard_start] + _bezier(guard_start, hose_point(.491, 1.104, .197),
                                        hose_point(.497, .942, .198), guard_end, 13)
        _pipe(kit, f"{name}/Trigger guard", guard, .014, dark, sides=10)
        kit.rod(f"{name}/Trigger lever", hose_point(.415, 1.064, .196),
                hose_point(.394, .999, .196), .010, steel, sides=10)
        spout = hose_point(.375, 1.15, .186)
        tip = hose_point(.293, 1.256, .133)
        path = [spout] + _bezier(spout, hose_point(.365, 1.23, .18), hose_point(.309, 1.260, .15), tip, 9)
        _pipe(kit, f"{name}/Stainless spout", path, .016, steel, sides=12)
        front = z + side * .185
        # The upstream display occupies y=1.21..1.41. Frame the console below it.
        kit.box(f"{name}/Payment face", (x, 1.063, front), (.382, .254, .051), dark, bevel=.018)
        kit.box(f"{name}/Card slot", (x + .095, 1.096, front + side * .031),
                (.104, .028, .023), steel, bevel=.007)
        kit.box(f"{name}/Card slot recess", (x + .095, 1.097, front + side * .044),
                (.073, .006, .003), rubber, bevel=.001)
        diode = front + side * .027
        kit.mesh(f"{name}/Reader telltale",
                 [(x + .07, 1.132, diode), (x + .12, 1.132, diode),
                  (x + .12, 1.144, diode), (x + .07, 1.144, diode)],
                 [(0, 1, 2, 3) if side == 1 else (3, 2, 1, 0)], lamp)
        for column in range(3):
            for row in range(3):
                kit.box(f"{name}/Key pad", (x - .129 + column * .036, 1.015 + row * .031, front + side * .027),
                        (.026, .022, .012), steel, bevel=.003, segments=1)
        kit.box(f"{name}/Kickplate", (x, .415, front), (.465, .197, .028), steel, bevel=.012)
        for dx in (-.198, .198):
            kit.rod(f"{name}/Kickplate fastener", (x + dx, .469, front + side * .012),
                    (x + dx, .469, front + side * .020), .008, dark, sides=8)


def _station(kit):
    finish = kit.mat("Station/Ribbed coated canopy", "#697782", .43, .59)
    seam = kit.mat("Station/Canopy lock seam", "#576572", .39, .67)
    steel = kit.mat("Station/Brushed equipment steel", "#a8b3b5", .28, .86)
    dark = kit.mat("Station/Housings and safety rings", "#373c43", .39, .42)
    rubber = kit.mat("Station/Fuel hose rubber", "#222631", .53, .09)
    nozzle_red = kit.mat("Station/Regular nozzle enamel", "#bd5340", .33, .34)
    nozzle_blue = kit.mat("Station/Super nozzle enamel", "#336580", .34, .38)
    lamp = kit.mat("Station/Payment indicator", "#b7fff4", .38, emission=(.6, 2.2, 1.9))
    yellow = kit.mat("Station/Hazard yellow enamel", "#c99635", .35, .35)
    trim_light = kit.mat("Station/Cyan canopy edge strip", "#51ddff", .36, emission=(.45, 5.1, 7.4))
    for index in range(8):
        x = 1.41 + index * .559
        kit.box("Station/Standing seam roof sheet", (x + .279, 4.31, -3.50), (.551, .030, 4.15),
                finish, bevel=.009)
        if index:
            kit.box("Station/Folded roof seam", (x, 4.333, -3.50), (.027, .045, 4.19),
                    seam, bevel=.007)
        for z in (-5.56, -1.44):
            kit.box("Station/Seam end closure", (x + .279, 4.328, z), (.557, .040, .046),
                    seam, bevel=.007)
            for offset in (.09, .45):
                kit.rod("Station/Canopy seam rivet", (x + offset, 4.338, z), (x + offset, 4.356, z),
                        .012, steel, sides=8)
    kit.box("Station/Roof access curb", (4.865, 4.36, -4.58), (.69, .090, .67), seam, bevel=.016)
    kit.box("Station/Roof access lid", (4.865, 4.415, -4.58), (.735, .049, .718), finish, bevel=.012)
    for x in (4.65, 5.075):
        kit.rod("Station/Access lid handle post", (x, 4.426, -4.58), (x, 4.481, -4.58),
                .018, steel, sides=10)
    kit.rod("Station/Access lid handle", (4.65, 4.481, -4.58), (5.075, 4.481, -4.58),
            .019, steel, sides=12)
    for x in (2.25, 3.65, 5.05):
        for z in (-4.4, -2.6):
            _downlight(kit, "Station/Canopy panel trim", (x, 3.845, z), (.9, .5),
                       finish, steel, None, backing=False)
    kit.box("Station/Street-facing canopy light strip", (3.65, 3.819, -1.414), (4.38, .026, .059),
            trim_light)
    kit.box("Station/Return canopy light strip", (5.89, 3.819, -3.52), (.059, .026, 4.15),
            trim_light)

    for island, z in enumerate((-4.35, -2.75)):
        for number, x in enumerate((3.05, 4.25)):
            _pump(kit, x, z, f"Station/Island {island + 1} pump {number + 1}", steel, dark, rubber,
                  nozzle_red if number == 0 else nozzle_blue, lamp)
        for x in (2.57, 4.73):
            kit.box("Station/Bollard flange", (x, .269, z), (.198, .024, .176),
                    dark, bevel=.009)
            for dx in (-.074, .074):
                for dz in (-.063, .063):
                    kit.rod("Station/Bollard anchor", (x + dx, .278, z + dz),
                            (x + dx, .292, z + dz), .014, steel, sides=8)
            for y in (.565, .775):
                _lathe(kit, "Station/Bollard contrast band", x, z,
                       [(y - .034, .060), (y - .031, .064),
                        (y + .031, .064), (y + .034, .060)], dark, sides=24, capped=False)
            _lathe(kit, "Station/Closed bollard crown", x, z,
                   [(1.001, .060), (1.014, .063), (1.039, .060),
                    (1.058, .044), (1.065, .018)], yellow, sides=24)

    # Fasten the down conductor to the street-facing column's outside face.
    start = (5.78, 3.832, -2.10)
    end = (5.43, 3.511, -2.10)
    path = [start] + _bezier(start, (5.58, 3.832, -2.10), (5.43, 3.70, -2.10), end, 12)
    path.append((5.43, .543, -2.10))
    _pipe(kit, "Station/Canopy roof drain", path, .041, seam, sides=18)
    for y in (.71, 1.78, 2.79):
        _lathe(kit, "Station/Drain clamp", 5.43, -2.10,
               [(y - .017, .042), (y - .012, .049), (y + .012, .049), (y + .017, .042)],
               steel, sides=20, capped=False)
    kit.box("Station/Drain cleanout box", (5.442, .535, -2.10), (.181, .195, .172),
            seam, bevel=.018)


def _street_lamps(scene_id, kit):
    # Lamps at pavement edges illuminate the visible façades and supply long
    # off-axis reflections in cars. Each diffuser emits only through its underside.
    sites = {
        "konbini": [(-6.15, 1.94, 3.55, .60, .36), (5.71, -1.7, 3.7, -.62, .11)],
        "kowloon": [(-5.90, 5.72, 3.3, .53, -.24), (6.52, 2.95, 3.0, -.53, -.20)],
        "station": [(-6.16, 1.90, 3.82, .65, -.16), (5.97, 6.92, 3.55, -.20, -.65)],
    }
    metal = kit.mat("Galvanized column and outreach", "#aab1b0", .33, .87)
    shoe = kit.mat("Cast iron column shoe", "#515858", .49, .84)
    reflector = kit.mat("Enamelled luminaire housing", "#626d70", .32, .21)
    amber = kit.mat("Warm street luminaire", "#ede3ce", .33, emission=(54, 35, 18))
    pearl = kit.mat("Neutral street luminaire", "#e4e5dc", .33, emission=(41, 39, 32))
    for i, (x, z, height, dx, dz) in enumerate(sites[scene_id]):
        kit.box("Footing", (x,.13,z), (.23,.25,.23), shoe, .018)
        kit.rod("Column", (x,.22,z), (x,height,z), .046, metal, 16)
        cx, cz = x+dx, z+dz
        kit.rod("Upswept outreach", (x,height-.19,z), (cx,height+.035,cz), .033, metal)
        kit.box("Overhanging lamp hood", (cx,height,cz), (.61,.11,.32), reflector, .036, 3)
        _downlight(kit, "Street luminaire", (cx,height-.059,cz), (.50,.25),
                   shoe, metal, amber if i == 0 else pearl)


def build(scene_id, kit):
    {"konbini": _konbini, "kowloon": _kowloon, "station": _station}[scene_id](kit)
    _street_lamps(scene_id, kit)
