from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

root = Path(__file__).resolve().parent.parent
output = root / 'artifacts' / 'RemoteDesk-source.zip'
output.parent.mkdir(exist_ok=True)
excluded = {'bin', 'obj', 'artifacts', '.git', 'data', '__pycache__', '.venv', 'build', 'dist', '.pytest_cache'}
count = 0
with ZipFile(output, 'w', ZIP_DEFLATED) as archive:
    for path in sorted(root.rglob('*')):
        if not path.is_file() or path.is_symlink():
            continue
        relative = path.relative_to(root)
        if excluded.intersection(relative.parts) or path.name == 'payload.zip':
            continue
        if path.suffix.lower() in {'.dpapi', '.pfx', '.part', '.zip', '.log', '.pem', '.key', '.p12'} or path.name == '.env' or path.name.endswith('.local.json'):
            continue
        # Investigation captures and original binary resource data stay outside the product/source package.
        if relative.parts[:2] == ('docs', 'evidence') and path.suffix != '.txt':
            continue
        archive.write(path, Path('RemoteDesk') / relative)
        count += 1
print(f'{output}: {count} source/documentation files')
