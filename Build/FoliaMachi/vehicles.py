"""Machi vehicles. Local coordinates are metres, with +Y up and +Z forward."""

from math import cos, hypot, pi, sin, sqrt, tau


def mix(a, b, t):
    return a + (b - a) * t


def add(a, b):
    return tuple(x + y for x, y in zip(a, b))


def sub(a, b):
    return tuple(x - y for x, y in zip(a, b))


def scaled(a, s):
    return tuple(x * s for x in a)


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def unit(a):
    return scaled(a, 1 / hypot(*a))


def sample_profile(rows, position):
    """Interpolate measured sections with monotone cubic tangents."""
    if position <= rows[0][0]:
        return rows[0][1:]
    if position >= rows[-1][0]:
        return rows[-1][1:]
    for index, (start, *left) in enumerate(rows[:-1]):
        end, *right = rows[index + 1]
        if position > end:
            continue
        previous, *before = rows[max(index - 1, 0)]
        following, *after = rows[min(index + 2, len(rows) - 1)]
        span = end - start
        t = (position - start) / span
        values = []
        for a, b, c, d in zip(before, left, right, after):
            slope = (c - b) / span
            left_slope = (c - a) / (end - previous)
            right_slope = (d - b) / (following - start)
            if slope == 0:
                values.append(b)
                continue
            m = span * slope * max(0, min(2, left_slope / slope))
            n = span * slope * max(0, min(2, right_slope / slope))
            values.append(mix(b, c, t * t * (3 - 2 * t)) + t * (1 - t) * (m * (1 - t) - n * t))
        return values


def patch(kit, name, material, point, columns, rows, flip=False, depth=None):
    """Tessellate a surface, optionally closing it with an offset inner skin."""
    stride = columns + 1
    vertices = [point(column / columns, row / rows) for row in range(rows + 1) for column in range(stride)]
    faces = []
    for row in range(rows):
        for column in range(columns):
            a = row * stride + column
            faces.append((a, a + 1, a + 1 + stride, a + stride))
    if depth:
        boundary = (list(range(stride)) + [row * stride + columns for row in range(1, rows + 1)]
                    + [rows * stride + column for column in range(columns - 1, -1, -1)]
                    + [row * stride for row in range(rows - 1, 0, -1)])
        size = len(vertices)
        vertices += [add(vertex, depth) for vertex in vertices]
        faces += [tuple(size + index for index in reversed(face)) for face in faces]
        for a, b in zip(boundary, boundary[1:] + boundary[:1]):
            faces.append((b, a, a + size, b + size))
    if flip:
        faces = [tuple(reversed(face)) for face in faces]
    kit.mesh(name, vertices, faces, material, True)


def pipe(kit, name, path, radius, material, sides=6):
    vertices = []
    for index, center in enumerate(path):
        tangent = unit(sub(path[min(index + 1, len(path) - 1)], path[max(0, index - 1)]))
        reference = (0, 1, 0) if abs(tangent[0]) > .8 else (1, 0, 0)
        normal = unit(cross(tangent, reference))
        bitangent = cross(tangent, normal)
        for step in range(sides):
            angle = tau * step / sides
            offset = add(scaled(normal, cos(angle) * radius), scaled(bitangent, sin(angle) * radius))
            vertices.append(add(center, offset))
    faces = []
    for ring in range(len(path) - 1):
        for index in range(sides):
            a = ring * sides + index
            b = ring * sides + (index + 1) % sides
            faces.append((a, b, b + sides, a + sides))
    faces += [tuple(reversed(range(sides))), tuple(range(len(vertices) - sides, len(vertices)))]
    kit.mesh(name, vertices, faces, material, True)


def arc(kit, name, x, y, z, profile, material, sides=40, span=(0, tau), closed=True):
    """Revolve an axial section about X, including the channel in the tyre tread."""
    lo, hi = span
    ring_size = sides if closed else sides + 1
    angles = [mix(lo, hi, step / sides) for step in range(ring_size)]
    vertices = [(x + offset, y + radius * cos(angle), z + radius * sin(angle))
                for offset, radius in profile for angle in angles]
    faces = []
    for ring in range(len(profile) - 1):
        for index in range(sides):
            a = ring * ring_size + index
            b = ring * ring_size + (index + 1) % ring_size
            faces.append((a, b, b + ring_size, a + ring_size))
    kit.mesh(name, vertices, faces, material, True)


def materials(kit):
    specifications = (
        ('paint', 'Vehicle paint', '#7e929f', .24, .38, None, 13),
        ('livery', 'Taxi livery', '#c69434', .32, .05, None, 13),
        ('cargo', 'Truck cargo enamel', '#c6ced1', .43, .48, None, 0),
        ('glass', 'Laminated vehicle glass', '#93a8b6', .12, 0, None, 1),
        ('rubber', 'Road tyre rubber', '#1d2225', .86, 0, None, 0),
        ('trim', 'Moulded black trim', '#252b2e', .51, 0, None, 0),
        ('liner', 'Wheel well liner', '#181c20', .91, 0, None, 0),
        ('seal', 'Rubber seals and panel gaps', '#151a1e', .63, 0, None, 0),
        ('alloy', 'Machined alloy', '#a3aeb4', .28, .73, None, 0),
        ('steel', 'Dull pressed steel', '#596169', .55, .76, None, 0),
        ('chrome', 'Polished trim', '#d1d6d7', .22, .86, None, 0),
        ('seat', 'Woven upholstery', '#575550', .84, 0, None, 0),
        ('seam', 'Upholstery piping', '#717066', .76, 0, None, 0),
        ('plate', 'Registration plate', '#dce1d7', .5, .05, None, 0),
        ('head', 'Headlight lens', '#ced9dd', .19, .15, None, 0),
        ('tail', 'Tail lamp lens', '#9e263c', .24, .1, None, 0),
        ('amber', 'Indicator lens', '#ba7023', .23, .08, None, 0),
        ('low', 'Headlight emitter', '#f1f2e5', .25, .04, (2.3, 2.14, 1.83), 0),
        ('stop', 'Taillight emitter', '#a92b3c', .25, .04, (1.46, .042, .086), 0),
        ('indicator', 'Indicator emitter', '#d58b24', .28, .03, (.28, .095, .012), 0),
    )
    return {key: kit.mat(name, color, roughness, metal, emission, kind)
            for key, name, color, roughness, metal, emission, kind in specifications}


def wheel(kit, m, side, x, y, z, radius, width, style):
    tread = width * .5
    lip = radius * (.65 if style == 'alloy' else .61)
    crown = [(u * tread, r * radius) for u, r in (
        (-.94, .65), (-1, .81), (-.90, .96), (-.64, 1),
        (-.44, 1), (-.41, .979), (-.34, .979), (-.31, 1),
        (.31, 1), (.34, .979), (.41, .979), (.44, 1), (.64, 1),
        (.90, .96), (1, .81), (.94, .65), (-.94, .65),
    )]
    count = 24 if style == 'small' else 28
    arc(kit, 'Tyre with two tread channels', x, y, z, crown, m['rubber'], count)
    rim_x = side * (tread - .015)
    rim = [(side * (-.045), lip * .93), (rim_x - side * .025, lip), (rim_x, lip),
           (rim_x + side * .008, lip * .94), (rim_x - side * .017, lip * .88)]
    arc(kit, 'Turned rim flange', x, y, z, rim if side > 0 else rim[::-1], m['alloy'], count)
    disc_x = x + side * (tread - .046)
    kit.rod('Brake rotor', (disc_x - .012, y, z), (disc_x + .012, y, z), lip * .77, m['steel'], 20)
    if style == 'alloy':
        for spoke in range(5):
            angle = tau * spoke / 5
            for branch in (-1, 1):
                inner = angle + branch * .07
                outer = angle + branch * .114
                a = (disc_x + side * .027, y + lip * .31 * cos(inner), z + lip * .31 * sin(inner))
                b = (x + rim_x - side * .010, y + lip * .9 * cos(outer), z + lip * .9 * sin(outer))
                kit.rod('Split alloy spoke', a, b, lip * .062, m['alloy'], 6)
    else:
        dish = [(rim_x - side * .025, lip * .92), (rim_x - side * .01, lip * .64),
                (rim_x + side * .006, lip * .45), (rim_x + side * .01, lip * .34)]
        arc(kit, 'Pressed wheel dish', x, y, z, dish if side > 0 else dish[::-1], m['steel'], count)
        for slot in range(6):
            angle = tau * slot / 6
            yy = y + lip * .72 * cos(angle)
            zz = z + lip * .72 * sin(angle)
            kit.rod('Pressed wheel vent', (x + rim_x - side * .021, yy, zz),
                    (x + rim_x - side * .009, yy, zz), lip * .095, m['liner'], 6)
    hub = x + rim_x + side * .009
    kit.rod('Hub cap', (disc_x, y, z), (hub, y, z), lip * .37, m['alloy'], 14)
    for stud in range(5):
        angle = tau * stud / 5
        yy = y + lip * .24 * cos(angle)
        zz = z + lip * .24 * sin(angle)
        kit.rod('Wheel nut', (hub, yy, zz), (hub + side * .009, yy, zz), lip * .047, m['steel'], 6)


def write_wheels(kit, m, half_width, axles, radius, width, style):
    wheel_x = half_width - width * .46
    for z in axles:
        kit.rod('Axle and suspension cross tube', (-wheel_x, radius, z), (wheel_x, radius, z), radius * .24, m['steel'], 12)
        for side in (-1, 1):
            wheel(kit, m, side, side * wheel_x, radius, z, radius, width, style)


def shell(kit, m, sections, axles, radius, floor, cabin_opening):
    """Loft the crowned body sides above real wheel openings and fold their edges inward."""
    reach = radius + .043
    rear, front, opening_width = cabin_opening
    stations = ({row[0] for row in sections} | {rear, front}
                | {mix(sections[0][0], sections[-1][0], i / 23) for i in range(24)}
                | {axle + reach * cos(pi * i / 14) for axle in axles for i in range(15)})
    edge = sorted({round(z, 8) for z in stations})
    edge = [z for z in edge if sections[0][0] <= z <= sections[-1][0]]

    def verticals(z):
        width, shoulder = sample_profile(sections, z)
        offset = min(abs(z - axle) for axle in axles)
        rise = sqrt(max(0, 1 - (offset / reach) ** 2))
        foot = mix(floor, radius + reach, rise)
        return width, shoulder, foot

    def flank(side, z, t):
        width, shoulder, foot = verticals(z)
        x = width - .061 * (1 - t) + .051 * sin(pi * t)
        return side * x, mix(foot, shoulder, t), z

    for side in (-1, 1):
        rows = 5
        vertices = [flank(side, z, row / rows) for row in range(rows + 1) for z in edge]
        strip = len(edge)
        faces = [(a, a + 1, a + 1 + strip, a + strip)
                 for row in range(rows) for a in range(row * strip, (row + 1) * strip - 1)]
        if side > 0:
            faces = [tuple(reversed(face)) for face in faces]
        kit.mesh('Formed outer quarter panels', vertices, faces, m['paint'], True)
        for axle in axles:
            path = [add(flank(side, z, 0), (side * .008, .005, 0))
                    for z in edge if abs(z - axle) <= reach + .00001]
            pipe(kit, 'Rolled fender opening lip', path, .011, m['paint'])
            tucked = [add(point, (-side * .11, -.018, 0)) for point in path]
            faces = [(i, i + 1, len(path) + i + 1, len(path) + i) for i in range(len(path) - 1)]
            if side > 0:
                faces = [tuple(reversed(face)) for face in faces]
            kit.mesh('Wheel arch liner return', path + tucked, faces, m['liner'], True)

    def deck(name, stations, left, right, steps):
        vertices = []
        for z in stations:
            width, shoulder, _ = verticals(z)
            for step in range(steps + 1):
                x = mix(left(width), right(width), step / steps)
                vertices.append((x, shoulder + (1 - (x / width) ** 2) * .062, z))
        stride = steps + 1
        faces = [(a, a + stride, a + 1 + stride, a + 1) for row in range(len(stations) - 1)
                 for a in range(row * stride, row * stride + steps)]
        kit.mesh(name, vertices, faces, m['paint'], True)

    center = [z for z in edge if rear <= z <= front]
    deck('Crowned rear deck', [z for z in edge if z <= rear], lambda w: -w, lambda w: w, 10)
    deck('Crowned front deck', [z for z in edge if z >= front], lambda w: -w, lambda w: w, 10)
    deck('Left window sill', center, lambda w: -w, lambda w: -opening_width, 2)
    deck('Right window sill', center, lambda w: opening_width, lambda w: w, 2)
    for end in (-1, 1):
        z = edge[-1] if end == 1 else edge[0]
        width, shoulder, foot = verticals(z)
        patch(kit, 'Radiused front or rear panel', m['paint'],
              lambda u, v: ((u * 2 - 1) * flank(1, z, v)[0],
                            mix(foot, shoulder + (1 - (u * 2 - 1) ** 2) * .062, v), z),
              10, 5, end < 0)
    return flank, verticals


def car_cabin(kit, m, y, roof_height, rear, front, roof_rear, roof_front, width, divider):
    roof_width = width * .83

    def side_point(side, u, v):
        lo = mix(rear, roof_rear, v)
        hi = mix(front, roof_front, v)
        x = mix(width, roof_width, v) + .025 * sin(pi * v) * (.7 + .3 * sin(pi * u))
        return side * x, mix(y, roof_height, v) - .012 * abs(2 * u - 1), mix(lo, hi, u)

    for side in (-1, 1):
        jambs = (0, .055, divider - .023, divider + .025, .936, 1)
        for index, (lo, hi) in enumerate(zip(jambs, jambs[1:])):
            point = lambda u, v: side_point(side, mix(lo, hi, u), v)
            is_glass = index in (1, 3)
            panel = (lambda u, v: point(u, mix(.052, .938, v))) if is_glass else point
            patch(kit, 'Formed side glazing' if is_glass else 'Painted roof pillar',
                  m['glass' if is_glass else 'paint'], panel, 7 if is_glass else 2, 5,
                  side > 0, (-side * .008, 0, 0) if is_glass else None)
        for v in (.027, .963):
            path = [side_point(side, i / 9, v) for i in range(10)]
            pipe(kit, 'Window weatherstrip', path, .013, m['seal'])
        for u in (.055, .936, divider - .023, divider + .025):
            path = [side_point(side, u, i / 6) for i in range(7)]
            pipe(kit, 'Glazing edge bead', path, .009, m['seal'])

    for end in (0, 1):
        sign = 1 if end else -1

        def windscreen(u, v):
            edge = side_point(1, end, v)
            x = 2 * u - 1
            crown = (1 - x * x)
            return edge[0] * x, edge[1] + .05 * crown, edge[2] + sign * crown * (.071 - v * .021)

        def glazing(u, v):
            return windscreen(mix(.017, .983, u), mix(.031, .956, v))

        patch(kit, 'Laminated windscreen' if end else 'Backlight glazing', m['glass'], glazing,
              14, 7, not end, (0, 0, -sign * .008))
        for fixed in (.017, .983):
            pipe(kit, 'Windscreen side seal', [windscreen(fixed, i / 7) for i in range(8)], .015, m['seal'])
        for fixed in (.022, .974):
            pipe(kit, 'Windscreen cross seal', [windscreen(i / 12, fixed) for i in range(13)], .015, m['seal'])
        if end:
            for u in (.23, .65):
                pipe(kit, 'Wiper arm', [add(windscreen(a, b), (0, 0, .014))
                                       for a, b in ((u, .012), (u + .075, .10), (u + .12, .108))], .007, m['seal'])
                pipe(kit, 'Windshield wiper blade', [add(windscreen(a, .106), (0, 0, .015))
                                                    for a in (u + .015, u + .11, u + .21)], .009, m['seal'])

    def top(u, v):
        x = 2 * u - 1
        return roof_width * x, roof_height + .054 * (1 - x * x) + .018 * sin(pi * v), mix(roof_rear, roof_front, v)

    patch(kit, 'Stamped roof crown', m['paint'], top, 12, 14, True, (0, -.028, 0))


def seats(kit, m, seat_front, seat_rear, track, size, floor, front):
    for side in (-1, 1):
        x = side * track
        kit.box('Front seat cushion', (x, floor + .045, seat_front), (size, .11, .34), m['seat'], .046, 3)
        patch(kit, 'Reclined front seat back', m['seat'],
              lambda u, v: (x + (u - .5) * size * (1 - .12 * v), floor + .077 + .266 * v,
                            seat_front - .13 - .068 * v + .032 * sin(pi * u)), 5, 5,
              depth=(0, 0, -.08))
        kit.box('Seat head restraint', (x, floor + .368, seat_front - .182), (size * .62, .13, .095), m['seat'], .042, 3)
        for edge in (-1, 1):
            pipe(kit, 'Seat bolster edge', [(x + edge * size * .39, floor + .103, seat_front + .119),
                                           (x + edge * size * .43, floor + .11, seat_front + .057),
                                           (x + edge * size * .42, floor + .106, seat_front - .12)], .008, m['seam'])
    if seat_rear is not None:
        kit.box('Rear bench cushion', (0, floor + .02, seat_rear), (track * 2 + size, .13, .31), m['seat'], .049, 3)
        kit.box('Rear bench backrest', (0, floor + .19, seat_rear - .133), (track * 2 + size, .29, .12), m['seat'], .046, 3)
        for side in (-1, 1):
            kit.box('Rear head restraint', (side * track, floor + .345, seat_rear - .15), (.17, .11, .10), m['seat'], .035, 2)
    kit.box('Dashboard and instrument binnacle', (0, floor + .25, front), (track * 2 + size, .13, .21), m['trim'], .043, 3)
    kit.box('Instrument hood', (track, floor + .323, front - .045), (.23, .06, .11), m['liner'], .02)
    steering = [(track + .111 * cos(tau * i / 18), floor + .245 + .105 * sin(tau * i / 18),
                 front - .18 + .035 * sin(tau * i / 18)) for i in range(19)]
    pipe(kit, 'Steering wheel', steering, .015, m['trim'])
    kit.rod('Steering column', (track, floor + .238, front - .19), (track, floor + .18, front), .028, m['trim'])
    kit.box('Steering wheel boss', (track, floor + .24, front - .19), (.14, .055, .031), m['trim'], .015)


def lamps(kit, m, width, front, rear, front_y, rear_y, truck=False):
    for end, z, height in ((1, front, front_y), (-1, rear, rear_y)):
        for side in (-1, 1):
            span = width * (.39 if end == 1 else .35)
            inner = width - span - .035
            lamp_height = .143 if truck and end == 1 else .096

            def lens(u, v):
                rounded = sin(pi * u)
                x = inner + u * span
                y = height + lamp_height * ((v - .5) * (.7 + .3 * rounded)) + .031 * u
                return side * x, y, z + end * (.018 * rounded - .075 * u ** 3)

            patch(kit, 'Wraparound headlamp lens' if end == 1 else 'Wraparound tail lamp lens',
                  m['head' if end == 1 else 'tail'], lens, 9, 3, side * end < 0, (0, 0, -end * .025))
            for index in range(3):
                lo = .09 + index * .21
                hi = lo + .188
                patch(kit, 'Individual lamp light guides', m['low' if end == 1 else 'stop'],
                      lambda u, v: add(lens(mix(lo, hi, u), mix(.24, .72, v)), (0, 0, end * .003)),
                      3, 1, side * end < 0)
            patch(kit, 'Amber indicator segment', m['indicator'],
                  lambda u, v: add(lens(mix(.76, .945, u), mix(.24, .72, v)), (0, 0, end * .003)), 4, 1, side * end < 0)


def fascia(kit, m, half, width, truck=False):
    for end in (-1, 1):
        for name, low, high, material in (
            ('Lower bumper valance', .235, .32, m['trim']),
            ('Painted bumper face', .32, .46, m['paint']),
        ):
            offset = .26 if truck else 0

            def point(u, v):
                x = u * 2 - 1
                return x * width * (.89 + .055 * sin(pi * v)), mix(low, high, v) + offset

            patch(kit, name, material,
                  lambda u, v: (*point(u, v), end * (half + .02 + .02 * sin(pi * v) - .16 * abs(u * 2 - 1) ** 4)),
                  14, 3, end < 0, (0, 0, -end * .045))
        plate_y = .41 if not truck else .697
        kit.box('Registration plate carrier', (0, plate_y, end * (half + .038)), (.269, .122, .021), m['trim'], .007)
        kit.box('Registration plate', (0, plate_y, end * (half + .051)), (.226, .096, .009), m['plate'], .005)
        for index in (-2, -1, 1, 2):
            kit.box('Registration character insert', (index * .034, plate_y - .012, end * (half + .056)),
                    (.013, .038, .002), m['steel'], .001, 1)
    grille_y = .562 if not truck else 1.114
    kit.box('Recessed grille well', (0, grille_y, half + .009), (width * 1.05, .165 if not truck else .31, .035), m['liner'], .022)
    slats = 7 if truck else 4
    for index in range(slats):
        kit.box('Grille fin', (0, grille_y + (index - (slats - 1) / 2) * .035, half + .029),
                (width * .98, .012, .014), m['chrome'], .003, 1)


def passenger_car(kit, m, kind):
    taxi = kind == 'taxi'
    traffic = kind == 'traffic'
    half, width = (1.65, .73) if traffic else (1.6, .71)
    radius = .27
    axles = (-1.05, 1.05) if traffic else (-.98, .98)
    cab_rear = -.98 if taxi else -.91 if traffic else -.97
    cab_front = .69 if taxi else .72 if traffic else .72
    shoulder = .776 if taxi else .77 if traffic else .767
    sections = (
        (-half, width - .154, .638), (-half + .12, width - .054, .72),
        (-1.24, width - .003, .755), (-.84, width, shoulder), (-.43, width - .023, shoulder),
        (.40, width - .022, shoulder - .008), (.86, width + .002, .752),
        (1.25, width - .006, .707), (half - .11, width - .053, .649), (half, width - .151, .60),
    )
    flank, _ = shell(kit, m, sections, axles, radius, .205, (cab_rear + .025, cab_front - .026, width - .114))
    kit.box('Underbody and floor pan', (0, .31, 0), (1.01, .16, half * 1.83), m['liner'], .034)
    kit.box('Exhaust silencer', (-.30, .221, -1.3), (.26, .085, .29), m['steel'], .035)
    kit.rod('Recessed exhaust tailpipe', (-.3, .248, -half + .2), (-.3, .248, -half - .017), .033, m['steel'], 18)
    write_wheels(kit, m, width, axles, radius, .19, 'steel' if taxi else 'alloy')
    roof_y = 1.285 if taxi else 1.289 if traffic else 1.266
    roof_rear, roof_front = (-.728, .386) if taxi else (-.669, .37) if traffic else (-.644, .327)
    car_cabin(kit, m, shoulder + .025, roof_y, cab_rear, cab_front, roof_rear, roof_front, width - .085, .52)
    seats(kit, m, .17, -.555, width * .412, .35, .644, .54)
    fascia(kit, m, half, width)
    lamps(kit, m, width, half - .006, -half + .003, .595, .624)
    for side in (-1, 1):
        for line, start, end in (('Rear door shut line', -.65, -.72), ('Centre door shut line', -.12, -.22),
                                 ('Front door shut line', .67, .58)):
            path = [add(flank(side, mix(start, end, i / 7), mix(.965, .12, i / 7)), (side * .002, 0, 0))
                    for i in range(8)]
            pipe(kit, line, path, .006, m['seal'], 6)
        pipe(kit, 'Sill break under the doors', [add(flank(side, mix(-.725, .583, i / 13), .12), (side * .002, 0, 0))
                                                for i in range(14)], .005, m['seal'], 6)
        if taxi:
            patch(kit, 'Taxi painted belt stripe', m['livery'],
                  lambda u, v: add(flank(side, mix(-1.37, 1.33, u), mix(.66, .795, v)), (side * .003, 0, 0)),
                  22, 2, side > 0)
        for z in (-.52, .062):
            x, y, _ = flank(side, z, .84)
            kit.box('Flush door handle recess', (x, y, z), (.017, .03, .122), m['seal'], .009)
            kit.box('Door pull', (x + side * .01, y + .004, z), (.016, .012, .113), m['chrome'], .005)
        kit.rod('Mirror stalk', (side * (width - .08), .864, .56), (side * (width + .04), .93, .57), .022, m['trim'])
        kit.box('Door mirror shell', (side * (width + .051), .943, .57), (.136, .07, .11), m['paint'], .031, 3)
        kit.box('Mirror glass', (side * (width + .052), .943, .518), (.108, .050, .008), m['chrome'], .012)
    for end, z0, z1 in ((1, .75, 1.53), (-1, -1.04, -1.53)):
        for side in (-1, 1):
            path = []
            for index in range(10):
                t = index / 9
                z = mix(z0, z1, t)
                section_width, y = sample_profile(sections, z)
                u = mix(.82, .9, t)
                path.append((side * u * section_width, y + .062 * (1 - u * u) + .001, z))
            pipe(kit, 'Bonnet and boot shut line', path, .005 if end > 0 else .006, m['seal'], 6)


def kei_truck(kit, m):
    half, width = 1.5, .68
    axles = (-1.0, 1.0)
    sections = ((-half, .588, .653), (-1.4, .664, .712), (-1.11, .671, .729), (-.75, .66, .727),
                (.25, .657, .708), (.62, .67, .743), (1.1, .675, .77), (1.44, .642, .748), (half, .593, .712))
    flank, _ = shell(kit, m, sections, axles, .27, .278, (.468, 1.428, .58))
    write_wheels(kit, m, width, axles, .27, .175, 'small')
    kit.box('Ladder frame', (0, .404, -.02), (.83, .19, 2.79), m['liner'], .026)
    car_cabin(kit, m, .752, 1.42, .445, 1.456, .483, 1.30, .63, .355)
    seats(kit, m, .90, None, .28, .34, .776, 1.28)
    fascia(kit, m, half, width)
    lamps(kit, m, width, half - .004, -half + .006, .631, .608)
    kit.box('Pressed steel cargo bed', (0, .757, -.526), (1.31, .059, 1.892), m['cargo'], .021)
    for index in range(-5, 6):
        kit.box('Cargo bed drainage rib', (index * .106, .792, -.528), (.032, .018, 1.74), m['steel'], .006, 1)
    for side in (-1, 1):
        kit.box('Hinged drop side', (side * .654, .883, -.526), (.048, .267, 1.89), m['paint'], .019, 3)
        kit.box('Drop side return flange', (side * .675, .978, -.526), (.024, .041, 1.86), m['steel'], .008, 2)
        for z in (-1.32, -.59, .264):
            kit.box('Cargo gate latch', (side * .684, .842, z), (.032, .077, .06), m['steel'], .008, 2)
        for z in (.53, 1.39):
            pipe(kit, 'Cab door seam', [add(flank(side, z - .045 * (1 - i / 12), i / 12), (side * .001, 0, 0))
                                        for i in range(3, 13)], .005, m['seal'], 6)
        kit.box('Cab door handle', (side * .668, .793, .63), (.033, .027, .112), m['trim'], .009)
        kit.rod('Mirror bracket', (side * .607, 1.023, 1.324), (side * .736, 1.019, 1.357), .014, m['trim'])
        kit.box('Commercial mirror', (side * .745, 1.019, 1.357), (.094, .117, .051), m['trim'], .021, 3)
        kit.box('Commercial mirror glass', (side * .747, 1.019, 1.329), (.074, .094, .006), m['chrome'], .002)
    kit.box('Hinged tailgate', (0, .895, -1.462), (1.29, .258, .064), m['paint'], .02, 3)
    kit.box('Cab load guard frame', (0, .961, .395), (1.26, .347, .045), m['paint'], .021)
    for side in (-1, 1):
        kit.rod('Load guard upright', (side * .545, 1.083, .396), (side * .545, 1.468, .396), .027, m['paint'])
    kit.rod('Load guard top crossbar', (-.552, 1.483, .396), (.552, 1.483, .396), .026, m['paint'])


def advertising_truck(kit, m):
    # These dimensions keep the cargo screen and running lights at their existing world planes.
    half, width = 3.475, 1.05
    front_axle = 2.575
    sections = ((1.812, .864, 1.562), (1.841, .951, 1.59), (2.23, .977, 1.59),
                (2.88, .987, 1.581), (3.31, .975, 1.55), (3.43, .938, 1.53), (half, .876, 1.481))
    flank, _ = shell(kit, m, sections, (front_axle,), .45, .488, (1.863, 3.402, .866))
    car_cabin(kit, m, 1.54, 2.681, 1.844, 3.43, 1.881, 3.135, .916, .26)
    seats(kit, m, 2.56, None, .426, .44, 1.36, 3.17)
    kit.box('Cab rear firewall', (0, 1.862, 1.827), (1.83, .737, .064), m['paint'], .035, 2)
    fascia(kit, m, half, width, True)
    lamps(kit, m, width, half - .004, -half + .014, .871, .78, True)
    axles = (-2.525, -1.525, front_axle)
    write_wheels(kit, m, width - .04, axles, .45, .34, 'steel')
    for side in (-1, 1):
        x = side * .436
        kit.box('Frame rail', (x, .581, -.084), (.14, .194, 6.55), m['steel'], .023, 2)
        for z in (-2.96, -.81):
            kit.box('Rear mudflap', (side * .865, .336, z), (.346, .341, .032), m['rubber'], .01)
        arc(kit, 'Rear tandem mudguard', side * .848, .45, -2.525,
            [(-.195, .507), (-.166, .539), (.166, .539), (.195, .507)], m['trim'], 24, (-1.16, .86), False)
        arc(kit, 'Rear tandem mudguard', side * .848, .45, -1.525,
            [(-.195, .507), (-.166, .539), (.166, .539), (.195, .507)], m['trim'], 24, (-.86, 1.16), False)
        pipe(kit, 'Door and wheel recess break', [add(flank(side, 2.24, i / 12), (side * .003, 0, 0))
                                                   for i in range(2, 13)], .009, m['seal'], 8)
        kit.box('Cab door latch recess', (side * .975, 1.46, 2.243), (.022, .065, .15), m['trim'], .019)
        kit.box('Cab door pull', (side * .991, 1.47, 2.244), (.025, .031, .139), m['chrome'], .011)
        kit.box('Entry step', (side * .886, .538, 2.064), (.34, .09, .447), m['steel'], .02)
        for groove in range(4):
            kit.box('Entry step grip', (side * .936, .589, 1.929 + groove * .077), (.225, .013, .02), m['rubber'], .004, 1)
        pipe(kit, 'Mirror support arm', [(side * .893, 2.134, 3.12), (side * 1.045, 2.134, 3.272),
                                          (side * 1.149, 1.954, 3.292)], .022, m['trim'])
        kit.box('Truck mirror housing', (side * 1.149, 1.963, 3.279), (.146, .315, .085), m['trim'], .034, 3)
        kit.box('Truck mirror silver', (side * 1.149, 1.963, 3.233), (.112, .273, .012), m['chrome'], .004)
    for z in (-2.86, -1.6, -.42, 1.0, 2.21):
        kit.box('Ladder chassis crossmember', (0, .606, z), (1.0, .115, .17), m['steel'], .021)
    kit.box('Truck rear underrun bumper', (0, .441, -3.421), (1.942, .168, .139), m['steel'], .025, 3)
    kit.rod('Fuel tank shell', (-.653, .538, .488), (-.653, .538, 1.495), .246, m['steel'], 36)
    for z in (.6, 1.37):
        kit.rod('Fuel tank strap', (-.653, .538, z - .028), (-.653, .538, z + .028), .252, m['trim'], 32)
    pipe(kit, 'Exhaust route', [(-.845, .597, .887), (-.845, .61, 1.6), (-.845, .785, 1.724),
                                (-.845, 3.1, 1.724)], .059, m['steel'], 18)
    kit.rod('Heat shield', (-.845, 1.011, 1.724), (-.845, 2.42, 1.724), .072, m['chrome'], 18)
    kit.box('Cargo enclosure', (0, 2.2, -.900), (2.086, 2.394, 5.137), m['cargo'], .064, 4)
    for y in (1.038, 3.363):
        kit.box('Cargo lower and upper rubrails', (0, y, -.9), (2.104, .073, 5.133), m['alloy'], .019, 3)
    for side in (-1, 1):
        kit.box('Rear door panel', (side * .512, 2.18, -3.484), (.994, 2.19, .043), m['cargo'], .024, 3)
        kit.rod('Cargo door lock bar', (side * .33, 1.211, -3.53), (side * .33, 3.206, -3.53), .021, m['steel'], 16)
        for y in (1.31, 2.176, 3.051):
            kit.box('Rear cargo door hinge', (side * .977, y, -3.513), (.132, .078, .062), m['steel'], .012)
        kit.box('Lock bar handle', (side * .23, 1.485, -3.568), (.219, .039, .026), m['alloy'], .011)
    for index in range(18):
        z = mix(-3.26, 1.462, index / 17)
        kit.box('Cargo roof stiffener', (0, 3.406, z), (1.943, .031, .035), m['cargo'], .011, 2)


def scooter_shell(kit, name, material, sections, columns=34, rings=30):
    """Elliptical transverse stations for compact pressed and moulded scooter parts."""
    start, end = sections[0][0], sections[-1][0]
    points = []
    for step in range(rings + 1):
        z = mix(start, end, step / rings)
        width, center, height = sample_profile(sections, z)
        for around in range(columns):
            theta = tau * around / columns
            points.append((width * cos(theta), center + height * sin(theta), z))
    faces = []
    for ring in range(rings):
        for step in range(columns):
            a = ring * columns + step
            b = ring * columns + (step + 1) % columns
            faces.append((a, b, b + columns, a + columns))
    faces += [tuple(reversed(range(columns))), tuple(range(len(points) - columns, len(points)))]
    kit.mesh(name, points, faces, material, True)


def scooter(kit, m):
    rear, front, radius = -.38, .5, .157
    def fairing(name, key, profile, rings=26):
        scooter_shell(kit, name, m[key], profile, 24, rings)

    fairing('Rear moulded fairing', 'paint', ((-.576, .028, .381, .034), (-.545, .103, .393, .091),
            (-.464, .146, .389, .145), (-.305, .161, .384, .156), (-.14, .144, .381, .129),
            (.001, .117, .375, .102), (.075, .048, .349, .05), (.088, .012, .345, .016)))
    fairing('Padded saddle', 'seat', ((-.49, .024, .532, .011), (-.456, .094, .55, .028),
            (-.335, .125, .559, .033), (-.144, .118, .572, .033), (.019, .088, .564, .022), (.06, .026, .55, .009)))
    fairing('Floor and frame covers', 'paint', ((-.19, .049, .235, .014), (-.131, .145, .23, .06),
            (.18, .137, .229, .057), (.33, .133, .23, .061), (.406, .10, .24, .057), (.444, .032, .242, .02)), 20)
    kit.box('Non-slip step-through floor', (0, .278, .136), (.252, .031, .518), m['rubber'], .013, 3)
    for n in range(7):
        kit.box('Floor grip rib', (0, .295, -.046 + n * .058), (.197, .008, .015), m['trim'], .003, 1)
    fairing('Compound curved leg shield', 'paint', ((.225, .007, .511, .083), (.262, .085, .511, .222),
            (.344, .14, .505, .278), (.404, .152, .51, .286), (.456, .12, .536, .217),
            (.512, .061, .623, .116), (.535, .017, .648, .052)))
    fairing('Handlebar nacelle', 'paint', ((.279, .035, .794, .029), (.31, .088, .796, .047),
            (.401, .109, .795, .059), (.493, .094, .789, .052), (.525, .025, .789, .026)), 20)
    for z in (rear, front):
        kit.rod('Wheel axle', (-.071, radius, z), (.071, radius, z), .028, m['steel'], 16)
        wheel(kit, m, 1, 0, radius, z, radius, .078, 'small')
        kit.rod('Inboard wheel hub', (-.041, radius, z), (-.003, radius, z), .05, m['alloy'], 24)
    arc(kit, 'Front wheel mudguard', 0, radius, front,
        [(-.079, .169), (-.063, .188), (-.029, .197), (.029, .197), (.063, .188), (.079, .169)],
        m['paint'], 28, (-1.49, 1.07), False)
    arc(kit, 'Rear internal mudguard', 0, radius, rear,
        [(-.072, .17), (-.056, .19), (.056, .19), (.072, .17)], m['trim'], 24, (-1.4, 1.52), False)
    for side in (-1, 1):
        x = side * .066
        kit.rod('Telescopic fork slider', (x, .16, .5), (x, .462, .416), .021, m['steel'], 16)
        kit.rod('Telescopic fork stanchion', (x, .39, .436), (x, .655, .36), .013, m['chrome'], 16)
        pipe(kit, 'Handlebar', [(side * .034, .801, .401), (side * .127, .81, .383),
                                (side * .226, .812, .358)], .011, m['steel'], 10)
        kit.rod('Handlebar rubber grip', (side * .173, .812, .374), (side * .242, .812, .353), .02, m['rubber'], 16)
        pipe(kit, 'Front brake lever', [(side * .149, .807, .404), (side * .188, .803, .409),
                                        (side * .236, .797, .385)], .005, m['alloy'], 8)
        kit.rod('Mirror stalk', (side * .105, .817, .384), (side * .204, .961, .402), .007, m['chrome'], 12)
        kit.box('Scooter mirror shell', (side * .207, .963, .408), (.106, .053, .023), m['trim'], .011, 3)
        kit.box('Scooter mirror glass', (side * .207, .963, .394), (.084, .041, .006), m['chrome'], .002)
    kit.rod('Rear suspension spring carrier', (.074, .167, -.363), (.111, .404, -.20), .023, m['steel'], 16)
    kit.box('Transmission cover', (-.085, .181, -.252), (.143, .143, .388), m['steel'], .053, 3)
    kit.box('Exhaust heat shield', (.095, .177, -.312), (.105, .098, .32), m['alloy'], .041, 3)
    kit.rod('Side stand', (-.062, .211, -.02), (-.192, .017, -.074), .013, m['steel'], 14)
    kit.box('Instrument lens', (0, .847, .397), (.117, .017, .084), m['seal'], .007, 3)
    kit.box('Scooter headlamp lens', (0, .788, .508), (.143, .065, .035), m['head'], .017, 3)
    kit.box('Scooter dipped beam', (0, .788, .527), (.104, .027, .008), m['head'], .004, 2)
    kit.box('Scooter rear light', (0, .428, -.554), (.167, .055, .041), m['tail'], .018, 3)
    kit.box('Scooter rear wheel number plate', (0, .331, -.565), (.128, .079, .015), m['plate'], .006, 2)
    for side in (-1, 1):
        kit.box('Amber turn indicator', (side * .061, .424, -.565), (.041, .034, .018), m['amber'], .005)


def build(model_id, kit):
    m = materials(kit)
    if model_id in ('sedan', 'taxi', 'traffic'):
        passenger_car(kit, m, model_id)
    elif model_id == 'kei':
        kei_truck(kit, m)
    elif model_id == 'ad-truck':
        advertising_truck(kit, m)
    elif model_id == 'scooter':
        scooter(kit, m)
    else:
        raise ValueError(f'Unknown Machi vehicle {model_id}')

