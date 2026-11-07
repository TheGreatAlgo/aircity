"""Export generated suitcase dimensions without rebaking the unchanged baggage props."""
import os
import sys
here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0,os.path.join(here,"Common"))
import vpipe
model = vpipe.load_model()
root, objects, materials = vpipe.build_objects(model)
vpipe.write_profile(model,list(objects.values()),os.path.join(here,"../../Assets/AirCity/Vehicles",model.NAME,model.NAME+".vehicle"))
