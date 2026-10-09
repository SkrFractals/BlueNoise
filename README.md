# BlueNoise  
Multi-dimensional, multi-length looping blue noise generator  
(development in progress, not usable yet, probably doesn't work yet)  
  
## Dimensions  
Efficiently packs arbitrary-length seamless loops in one dimension, and fixed-length seamless loops in an arbitrary number of more dimensions.  
  
## Generator + Sampler  
The generator can generate the efficient blue noise file. The standalone sampler can load the file and provide fast and seamless looping sampling.  
  
## Use-cases  
I'm making this for the Fractal Generator. I would use a 3D mode to generate a file, and the 3rd dimension would be the arbitrary length, which could provide seamless temporal loops over any user-chosen animation length. The 2 spatial dimensions can be a fixed length, since the generator doesn't loop the image edges like a torus, only the time needs to loop.  
But thanks to the arbitrary number of dimensions, it could do the same thing for volumetric animations.  
