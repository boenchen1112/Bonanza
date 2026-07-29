"""Throwaway: find the two red dot centroids in reference_2_4.png (pixel
space) so index.html can map the image onto REFERENCE[2]'s logical space
via a solved uniform-scale + translation, instead of a rough bounding-box
fit."""
import cv2
import numpy as np

path = "D:/VS Code/Swinger/research/hand_tracking_web/assets/reference_2_4.png"
img = cv2.imread(path)
print("image shape (h, w, c):", img.shape)
hsv = cv2.cvtColor(img, cv2.COLOR_BGR2HSV)

# Red wraps hue 0/179 -- two ranges, same approach as live_position_view.py's
# _hue_ranges().
lower1 = np.array([0, 80, 80]); upper1 = np.array([10, 255, 255])
lower2 = np.array([170, 80, 80]); upper2 = np.array([179, 255, 255])
mask = cv2.inRange(hsv, lower1, upper1) | cv2.inRange(hsv, lower2, upper2)

contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
contours = sorted(contours, key=cv2.contourArea, reverse=True)[:2]
centroids = []
for c in contours:
    M = cv2.moments(c)
    cx = M["m10"] / M["m00"]
    cy = M["m01"] / M["m00"]
    centroids.append((cx, cy, cv2.contourArea(c)))

# Sort by y (image space) descending -- beat 1 (lower point == larger y)
# comes before beat 2.
centroids.sort(key=lambda p: -p[1])
print("found", len(centroids), "red blobs (x_px, y_px, area):")
for c in centroids:
    print(" ", c)
