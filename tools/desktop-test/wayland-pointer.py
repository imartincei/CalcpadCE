import json
import os
import socket
import struct
import sys
import time


connection = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
connection.settimeout(10)
connection.connect(os.path.join(os.environ['XDG_RUNTIME_DIR'], os.environ['WAYLAND_DISPLAY']))
callback_id = 3


def send(object_id, opcode, payload=b''):
    connection.sendall(struct.pack('=II', object_id, (len(payload) + 8) << 16 | opcode) + payload)


def integers(*values):
    return struct.pack('=' + 'I' * len(values), *values)


def read(size):
    data = b''
    while len(data) < size:
        part = connection.recv(size - len(data))
        if not part:
            raise EOFError('Wayland compositor disconnected')
        data += part
    return data


def roundtrip(on_global=lambda *_: None):
    global callback_id
    current = callback_id
    callback_id += 1
    send(1, 0, integers(current))
    while True:
        object_id, header = struct.unpack('=II', read(8))
        opcode = header & 0xffff
        payload = read((header >> 16) - 8)
        if object_id == 1 and opcode == 0:
            raise RuntimeError(f'Wayland protocol error: {payload!r}')
        if object_id == current:
            return
        if object_id == 2 and opcode == 0:
            name, length = struct.unpack_from('=II', payload)
            interface = payload[8:8 + length - 1].decode()
            on_global(name, interface)


manager_name = None


def find_manager(name, interface):
    global manager_name
    if interface == 'zwlr_virtual_pointer_manager_v1':
        manager_name = name


send(1, 1, integers(2))
roundtrip(find_manager)
assert manager_name is not None, 'Compositor does not expose the virtual pointer protocol'
callback_id = 6
interface = b'zwlr_virtual_pointer_manager_v1\0'
encoded_interface = integers(len(interface)) + interface + b'\0' * (-len(interface) % 4)
send(2, 0, integers(manager_name) + encoded_interface + integers(1, 4))
send(4, 0, integers(0, 5))
roundtrip()
print('ready', flush=True)

for line in sys.stdin:
    command = json.loads(line)
    timestamp = int(time.monotonic() * 1000) & 0xffffffff
    if command['type'] == 'move':
        send(5, 1, integers(timestamp, command['x'], command['y'], command['width'], command['height']))
    elif command['type'] == 'button':
        send(5, 2, integers(timestamp, command['button'], int(command['pressed'])))
    else:
        raise ValueError(command)
    send(5, 4)
    roundtrip()
    print('ok', flush=True)
