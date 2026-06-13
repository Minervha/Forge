# Forge Linux — mise en place sur la VM

Procédure pour faire tourner `DataTableForge` sur la VM Linux cible (étapes 6-8 du [plan](plan.md)).
À exécuter **quand la VM existe**. Le bundle est self-contained → la VM n'a besoin d'**aucun runtime .NET**.

## 0. Prérequis VM
- Linux x86_64 (glibc — Ubuntu/Debian/Fedora… récents OK).
- `tar`, `wget`/`curl` pour récupérer repak. Rien d'autre.
- **Pas besoin de libicu** : le bundle est publié avec `InvariantGlobalization=true`
  (sinon .NET self-contained réclame `libicu` → `apt install libicu`). Validé sur Ubuntu 26.04.

## 1. Construire le bundle (sur Windows)
```powershell
pwsh ./publish-linux.ps1      # produit ./publish-linux/ (~77 Mo, ELF + runtime embarqué)
```

## 2. Transférer le bundle vers la VM
Un seul dossier à copier. Au choix :
```bash
# depuis Windows (scp) ou via un dossier partagé / rsync
scp -r ./publish-linux user@vm:/home/forge/app
```

## 3. Rendre le binaire exécutable
Le bit +x n'est pas préservé par tous les transferts :
```bash
cd /home/forge/app
chmod +x DataTableForge
```

## 4. Récupérer repak (Linux)
> ⚠️ **Aligner la version sur le repak que Studio utilise côté Windows** pour un packing identique.
> (`v0.2.3` ci-dessous = dernière release au moment du plan — à ajuster.)
```bash
REPAK_VER=v0.2.3
mkdir -p /home/forge/repak && cd /home/forge/repak
wget https://github.com/trumank/repak/releases/download/$REPAK_VER/repak_cli-x86_64-unknown-linux-gnu.tar.xz
tar xf repak_cli-x86_64-unknown-linux-gnu.tar.xz
chmod +x repak
./repak --version            # vérif
```

## 5. Config Linux
`forge.config.json` (chemins Linux, `repakExe` **sans** `.exe`) :
```json
{
  "buildDir":  "/home/forge/data/build",
  "modsDir":   "/home/forge/data/mods",
  "outputDir": "/home/forge/data/output",
  "usmapPath": "/home/forge/data/mappings.usmap",
  "repakExe":  "/home/forge/repak/repak",
  "pakName":   "MyMods",
  "userId":    "",
  "mods":      ["mod_folder_1"]
}
```

## 6. Lancer
```bash
# Génération d'un .pak :
./DataTableForge generate --config forge.config.json

# (ou) extraction des DataTables de base depuis un .pak de jeu — 4 args positionnels :
./DataTableForge <pak> <usmap> <output-dir> /home/forge/repak/repak
```

## 7. Vérifier
- `dotnet test` (si le SDK est présent sur la VM) ou rejouer la suite côté Windows — baseline : **35/35**.
- e2e : exit 0 + `{pakName}_P.pak` créé dans `outputDir`, idéalement chargé en jeu.

## Piège n°1 : la casse
Le FS Linux est **case-sensitive**. Si un `File.Exists` échoue (ex. un asset
`WildLifeC/Content/DataTables/...` introuvable alors qu'il existe sous Windows),
c'est un **mismatch de casse** entre le code et les noms de fichiers/dossiers sur disque.
→ Corriger côté **données** (renommer pour matcher la casse attendue par le code).
