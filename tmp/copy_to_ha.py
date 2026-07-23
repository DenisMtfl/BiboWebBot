import os
import posixpath
import traceback
import paramiko

HOST = '192.168.1.50'
USERNAME = 'root'
PASSWORD = 'cafeaffe99!'
SOURCE_ROOT = r'A:\BiboWebBot\publish\HomeAssistant'
TARGET_ROOT = '/config/netdaemon6'


def ensure_remote_dir(sftp, remote_dir):
    parts = remote_dir.strip('/').split('/')
    current = ''
    for part in parts:
        current = current + '/' + part if current else '/' + part
        try:
            sftp.stat(current)
        except IOError:
            sftp.mkdir(current)


try:
    print(f'Connecting to {HOST}...')
    ssh = paramiko.SSHClient()
    ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    ssh.connect(hostname=HOST, username=USERNAME, password=PASSWORD, timeout=30)
    print('Connected.')

    sftp = ssh.open_sftp()
    ensure_remote_dir(sftp, TARGET_ROOT)
    print(f'Target directory ready: {TARGET_ROOT}')

    for dirpath, dirnames, filenames in os.walk(SOURCE_ROOT):
        rel = os.path.relpath(dirpath, SOURCE_ROOT)
        remote_dir = TARGET_ROOT if rel == '.' else posixpath.join(TARGET_ROOT, rel.replace('\\', '/'))
        ensure_remote_dir(sftp, remote_dir)
        for filename in filenames:
            local_path = os.path.join(dirpath, filename)
            remote_path = posixpath.join(remote_dir, filename)
            print(f'Uploading {remote_path}')
            sftp.put(local_path, remote_path)

    sftp.close()
    ssh.close()
    print('DONE')
except Exception as exc:
    print('FAILED:')
    print(exc)
    traceback.print_exc()
    raise
