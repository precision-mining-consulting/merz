# merz
Mining Consulting Tooling

Welcome to the MERZ package; a set of tools tailored for mining engineers.
MERZ is designed as a set of 'plugins' for the Spry scheduling software, although
this is not a hard limitation.

The tool set is used internally by Precision Mining, and is available for general use with
regular updates at the [Releases](https://github.com/precision-mining-consulting/merz/releases) page.

## How to get it

MERZ should be downloaded from the releases page. This page should be checked regularly for updates.
Once the installer has been downloaded, run the installer to install the binary in the default location.
You may need administrator privileges.

To use the tooling, a script is required within a Spry model. This script is packaged with the installer.

1. Import a **Single Script** from the MERZ installation folder (`C:\Program Files\merz\MERZ.cs`)

<p align="center">
<img src="https://user-images.githubusercontent.com/13831379/202963751-5b338288-1ce6-4b38-92b0-8ba5f1b926ad.png"/>
</p>

2. After recompiling scripts, there will be a `Merz->Open()` script entry point:

<p align="center">
<img src="https://user-images.githubusercontent.com/13831379/202963892-2d00858d-43ed-493e-9557-f99932b26027.png"/>
</p>

3. Running this script (should) open a dialogue with various tools available (your dialogue may look a little different):

<p align="center">
<img src="https://user-images.githubusercontent.com/13831379/202963994-8e270a77-88de-441b-9fdb-8e18f2ab7148.png"/>
</p>


## Support

**MERZ is unsupported**. The tools are actively maintained by Precision Mining, since they are in frequent use internally,
but they are released with **NO WARRANTY** for general use.
It also should be mentioned that running the tools is at the user's own risk.

We encourage users to report bugs through the [issue tracker](https://github.com/precision-mining-consulting/merz/issues),
and feature requests will be actively considered.
Documentation for the tooling is currently sparse, with a goal to improve this.
Any exceptions or errors will be dumped to the Output panel, this is a good place to start.
