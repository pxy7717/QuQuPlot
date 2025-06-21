from PIL import Image
import os

def convert_png_to_ico(png_path, ico_path):
    """
    Convert PNG image to ICO format
    :param png_path: Path to the PNG file
    :param ico_path: Path where the ICO file will be saved
    """
    try:
        # Open the PNG image
        img = Image.open(png_path)
        
        # Convert to RGBA if not already
        if img.mode != 'RGBA':
            img = img.convert('RGBA')
        
        # Save as ICO
        img.save(ico_path, format='ICO', sizes=[(256, 256)])
        print(f"Successfully converted {png_path} to {ico_path}")
    except Exception as e:
        print(f"Error converting image: {str(e)}")

def main():
    # Get the current directory
    current_dir = os.path.dirname(os.path.abspath(__file__))
    
    # Define input and output paths
    png_file = os.path.join(current_dir, "logo.png")
    ico_file = os.path.join(current_dir, "logo.ico")
    
    # Check if the PNG file exists
    if not os.path.exists(png_file):
        print(f"Error: {png_file} not found!")
        return
    
    # Convert the image
    convert_png_to_ico(png_file, ico_file)

if __name__ == "__main__":
    main()
