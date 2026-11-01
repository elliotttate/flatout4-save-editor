# Regenerates Services/FlatOut4ControlNames.cs from the engine input header:
#   python scripts/gen_control_names.py <repo-root>/PlayAll/Code/Runtime/Common/PlayAll/Common/Input/InputStates.h Services/FlatOut4ControlNames.cs
import re
import sys

src, out = sys.argv[1], sys.argv[2]
lines = open(src, encoding='utf-8', errors='replace').read().splitlines()

KEY_NAMES = {
    'ESCAPE': 'Esc', 'BACK': 'Backspace', 'TAB': 'Tab', 'RETURN': 'Enter', 'LCONTROL': 'Left Ctrl',
    'RCONTROL': 'Right Ctrl', 'LSHIFT': 'Left Shift', 'RSHIFT': 'Right Shift', 'LMENU': 'Left Alt',
    'RMENU': 'Right Alt', 'SPACE': 'Space', 'CAPITAL': 'Caps Lock', 'NUMLOCK': 'Num Lock',
    'SCROLL': 'Scroll Lock', 'UP': 'Up arrow', 'DOWN': 'Down arrow', 'LEFT': 'Left arrow',
    'RIGHT': 'Right arrow', 'PRIOR': 'Page Up', 'NEXT': 'Page Down', 'HOME': 'Home', 'END': 'End',
    'INSERT': 'Insert', 'DELETE': 'Delete', 'LWIN': 'Left Windows', 'RWIN': 'Right Windows',
    'APPS': 'Menu', 'MINUS': 'Minus', 'EQUALS': 'Equals', 'LBRACKET': 'Left bracket',
    'RBRACKET': 'Right bracket', 'SEMICOLON': 'Semicolon', 'APOSTROPHE': 'Apostrophe', 'GRAVE': 'Grave',
    'BACKSLASH': 'Backslash', 'COMMA': 'Comma', 'PERIOD': 'Period', 'SLASH': 'Slash',
    'MULTIPLY': 'Numpad *', 'SUBTRACT': 'Numpad -', 'ADD': 'Numpad +', 'DECIMAL': 'Numpad .',
    'DIVIDE': 'Numpad /', 'NUMPADENTER': 'Numpad Enter', 'NUMPADEQUALS': 'Numpad =',
    'NUMPADCOMMA': 'Numpad ,', 'SYSRQ': 'Print Screen', 'MEDIASELECT': 'Media select', 'PAUSE': 'Pause', '10': '0',
}


def humanize(token):
    text = token.replace('_', ' ').lower()
    text = re.sub(r'([a-z])(\d)', r'\1 \2', text)
    text = re.sub(r'\b([xyz])\b', lambda m: m.group(1).upper(), text)
    return text[:1].upper() + text[1:]


def key_label(name):
    if name in KEY_NAMES:
        return KEY_NAMES[name]
    if re.fullmatch(r'\d', name):
        return name
    if re.fullmatch(r'F\d+', name):
        return name
    if re.fullmatch(r'[A-Z]', name):
        return name
    m = re.fullmatch(r'NUMPAD(\d)', name)
    if m:
        return 'Numpad ' + m.group(1)
    return humanize(name)


keyboard = {}
for line in lines:
    m = re.match(r'\s*static const DeviceControlId\s+I_ICI_KEYBOARD_(\w+)\s*=\s*(0x[0-9A-Fa-f]+|\d+)\s*;', line)
    if m:
        value = int(m.group(2), 0)
        keyboard.setdefault(value, key_label(m.group(1)))


def parse_enum(name, prefix):
    values = []
    inside = False
    for line in lines:
        if 'PA_BEGIN_ENUM_DECLARATION' in line and name in line:
            inside = True
            continue
        if inside and 'PA_END_ENUM_DECLARATION' in line:
            break
        if not inside or line.strip().startswith('//'):
            continue
        m = re.match(r'\s*PA_DECLARE_ENUM\(\s*' + prefix + r'(\w+)\s*\)\s*(?://\s*(.*))?', line)
        if m:
            values.append((m.group(1), (m.group(2) or '').strip()))
    return values


def pad_label(token, comment):
    fixed = {
        'LEFT_PAD_UP': 'D-pad up', 'LEFT_PAD_RIGHT': 'D-pad right', 'LEFT_PAD_DOWN': 'D-pad down',
        'LEFT_PAD_LEFT': 'D-pad left', 'RIGHT_PAD_UP': 'Y / Triangle', 'RIGHT_PAD_RIGHT': 'B / Circle',
        'RIGHT_PAD_DOWN': 'A / Cross', 'RIGHT_PAD_LEFT': 'X / Square', 'BUMPER_LEFT': 'LB / L1',
        'BUMPER_RIGHT': 'RB / R1', 'TRIGGER_LEFT': 'LT / L2', 'TRIGGER_RIGHT': 'RT / R2',
        'START': 'Start', 'SELECT': 'Back / Select', 'LEFT': 'Left stick click', 'RIGHT': 'Right stick click',
        'LEFT_STICK_X': 'Left stick X', 'LEFT_STICK_Y': 'Left stick Y', 'RIGHT_STICK_X': 'Right stick X',
        'RIGHT_STICK_Y': 'Right stick Y',
    }
    return fixed.get(token, humanize(token))


def wheel_label(token, comment):
    m = re.fullmatch(r'BUTTON(\d+)', token)
    if m:
        return 'Button ' + m.group(1)
    m = re.fullmatch(r'POV_(\w+)', token)
    if m:
        return 'POV ' + m.group(1).lower()
    label = humanize(token)
    if comment and re.search(r'[A-Za-z]', comment) and len(comment) < 30:
        label += ' (' + comment.strip().lower() + ')'
    return label


pad = [(i, pad_label(t, c)) for i, (t, c) in enumerate(parse_enum('GamePadDeviceControlId', 'I_ICI_GAMEPAD_'))]
wheel = [(i, wheel_label(t, c)) for i, (t, c) in enumerate(parse_enum('GenericDeviceControlId', 'I_ICI_GENERICDEVICE_'))]


def cs_string(text):
    return '"' + text.replace('\\', '\\\\').replace('"', '\\"') + '"'


def emit(name, pairs):
    body = ',\n'.join('        [%d] = %s' % (value, cs_string(label)) for value, label in pairs)
    return '    public static IReadOnlyDictionary<long, string> %s { get; } = new Dictionary<long, string>\n    {\n%s\n    };\n' % (name, body)


text = '''namespace FlatOut4SaveEditor.Services;

// Control ids stored in the binding tables, named from PlayAll/Common/Input/InputStates.h:
// keyboard ids are DirectInput scan codes, gamepad and wheel ids are the
// GamePadDeviceControlId / GenericDeviceControlId enum values.
// Generated from the engine header; regenerate rather than hand-edit.
public static class FlatOut4ControlNames
{
''' + emit('Keyboard', sorted(keyboard.items())) + '\n' + emit('Gamepad', pad) + '\n' + emit('SteeringWheel', wheel) + '}\n'

open(out, 'w', encoding='utf-8', newline='\n').write(text)
print('keyboard', len(keyboard), 'pad', len(pad), 'wheel', len(wheel))
