# Forge multiplateforme — build Linux self-contained + test sur VM

Status: `done` — étapes 1-8 **faites et validées** sur VM locale (WSL2 Ubuntu, depuis démontée). Forge Linux fonctionnelle.

## Avancement
- ✅ **1-3, 5** : `publish-linux.ps1` (testé, bundle 76 Mo ELF self-contained), `.gitignore` += `publish-linux/`, README MAJ, template de config + procédure VM dans [linux-setup.md](linux-setup.md).
- ✅ **4 (doc)** : repak Linux récupéré — **v0.2.3, = version Windows de Studio** (pas de désalignement).
- ✅ baseline tests : **35/35** sur Windows.
- ✅ **6-8 FAITS** sur VM locale **WSL2 Ubuntu 26.04** (e2e complet, données réelles : build 06.12 + 18 mods + usmap).

## Résultats du test e2e (VM WSL2)
- **ICU** : .NET self-contained ne bundle PAS libicu → corrigé via `InvariantGlobalization=true` dans
  `publish-linux.ps1`. **Le bundle n'a désormais besoin d'AUCUNE dépendance système.** (Alternative : `apt install libicu`.)
- **e2e** : exit 0, `Minervha_Mods_P.pak` = **1 005 303 607 octets**, 18 mods OK, fallback v1 JSON (pas d'`AutoMod_P`).
- **Casse** : ✅ **non-problème** sur ce jeu — pak Linux vs Windows = **même set de 987 fichiers, mêmes chemins exacts** (casse identique).
- **Parité contenu** : **981/987 fichiers bit-identiques** à Windows. Les 6 qui diffèrent = uniquement les 3 DataTables
  injectées (`DT_ClothesOutfit`, `DT_GameCharacterCustomization`, `DT_GameCharacterOutfits` × uasset/uexp).
- **Déterminisme** : ✅ 2 runs Linux → DataTables **bit-identiques**. La VM produit une sortie stable.

### Constat ouvert : reproductibilité cross-plateforme
Les 6 DataTables diffèrent Linux↔Windows car l'**ordre des lignes** dépend de l'ordre d'énumération des `.txt`
de mods (`Directory.GetFiles`), différent entre ext4 et NTFS. Sans impact en jeu (DataTables = maps clés→lignes),
**mais** : si deux mods partagent un `ClothingId`, le « last-wins » du dédoublonnage pourrait trancher différemment
selon la plateforme. Fix éventuel (1 ligne) : trier `ruleFiles` dans `ForgeGenerator` → sortie identique partout.
**Décision (2026-06-14) : NON retenu** — la parité fonctionnelle suffit, on ne touche pas à la logique de generate.
L'option reste documentée ici si la reproductibilité au bit devient nécessaire.

## Point de départ (état actuel)

- `DataTableForge` = console **.NET 8.0**, code **déjà multiplateforme** : `Path.Combine` partout,
  pas de P/Invoke, pas de registre, pas de GUI, `AppContext.BaseDirectory` pour la résolution de config.
- Dépendances **100 % managées / portables** : `UAssetAPI.dll` (C# pur), `Newtonsoft.Json`, `ZstdSharp.Port`.
- Seule dépendance **externe** : `repak`, lancé via `Process.Start` — chemin pris du **config** (`repakExe`),
  rien de hardcodé (`ForgeGenerator.cs:335`, `Program.cs:138`).
- `publish.ps1` produit `DataTableForge.exe` (framework-dependent) et le copie dans
  `…/Studio/resources/forge/DataTableExtractor_bin` (contrat Windows pour Studio).
- **Validé empiriquement** : `dotnet publish -r linux-x64 --self-contained true` depuis Windows
  produit un **ELF Linux fonctionnel** (77 Mo, runtime .NET embarqué, `UAssetAPI.dll` chargé, pas de `.exe`).

## Objectif final

- **Un seul codebase → deux artefacts** :
  - `win-x64` framework-dependent → Studio (**inchangé**).
  - `linux-x64` **self-contained** → VM Linux (la VM n'a besoin d'aucun runtime).
- Tout le pipeline (`extract` + `generate` + pack `repak`) tourne sur la **VM Linux cible** avec des données réelles.

## Périmètre

**IN**
- Script de publish Linux (self-contained `linux-x64`).
- Récupération + intégration de `repak` Linux (release GitHub, asset `…-unknown-linux-gnu.tar.xz`).
- Template de config Linux.
- Audit de **casse** des chemins d'assets hardcodés (seul vrai risque code sous Linux).
- `dotnet test` cross-platform + test e2e sur la VM.
- MAJ README (section Building).

**OUT**
- Toute modification de la **logique métier**.
- Toute refonte du build Windows / `publish.ps1` (reste **inchangé**).
- Le portage de **Studio** lui-même sous Linux (autre repo, autre sujet).
- CI/CD automatisé (plus tard si besoin).

## Étapes (avec critère de vérification)

| # | Étape | Verify |
|---|-------|--------|
| 1 | `publish-linux.ps1` : self-contained `linux-x64` → `./publish-linux/` | ELF + `libcoreclr.so`, pas de `.exe` — **déjà prouvé manuellement** |
| 2 | `.csproj` **inchangé** ; flags self-contained passés en `-p:` dans le script (override des flags anti-AV, qui ne servent qu'à Windows) | Build OK, comportement Windows intact |
| 3 | `.gitignore` : ajouter `publish-linux/` | `git status` propre |
| 4 | `repak` Linux : télécharger `repak_cli-x86_64-unknown-linux-gnu.tar.xz`, extraire, `chmod +x` | `./repak --version` sur la VM. **Aligner la version sur le repak Windows de Studio** |
| 5 | Template `forge.config.json` Linux : chemins Linux + `repakExe` sans `.exe` | La Forge trouve le config + repak |
| 6 | Transfert VM : `publish-linux/` + repak + données (build/mods/usmap/pak) | `chmod +x DataTableForge` puis `./DataTableForge` s'exécute |
| 7 | `dotnet test` | Tous les tests passent (VM ou Windows) |
| 8 | Test e2e sur la VM : `generate` → `.pak` produit | Exit 0, `.pak` créé, idéalement chargé en jeu |

### Risque principal : sensibilité à la casse
Sous Linux le FS est **case-sensitive**. Les sous-chemins d'assets sont hardcodés avec une casse précise,
ex. `WildLifeC/Content/DataTables/DT_ClothesOutfit.uasset` (`AssetInjector.cs:35`). Si un `File.Exists`
échoue à l'étape 8 → c'est un mismatch de casse entre le code et les données sur disque. Correction côté
**données** (renommer), pas côté code, sauf casse vraiment incohérente dans le repo.

## Question ouverte
Accès à la VM (SSH ?) pour que je pilote les étapes 6-8, ou tu les exécutes et me rapportes la sortie ?

## Note d'architecture
Pas de découpage en chunks : la tâche tient dans un seul contexte. Si elle grossit (CI, portage Studio),
on créera des sous-plans à ce moment-là.
