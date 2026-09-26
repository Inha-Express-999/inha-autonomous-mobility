# Sensor capture metadata and observed motion

Unity 6000.3.21f1 moving-pedestrian PlayMode 1/1 and EditMode 17/17 passed.
Python full suite 174/174 passed before the final diagnostic accessor/test addition;
then the targeted tracking suite 7/7 and Ruff passed. Source hashes distinguish
revisions; later diagnostic expiry/finite-output changes were tested in Python.

Live transport accepts capture time and sensor world pose while service missions
still complete. EditMode verifies physical metre units with a scaled sensor and
rejects tilt under the planar contract. Python tests verify ego-motion compensation,
side motion to circle TTC, context/gap reset, metadata coherence and expiry.
No full TTC actuation, center tracking/uncertainty, swept footprint, occluded-motion
prediction, Player or original scene claim. Existing runtime warnings remain.
