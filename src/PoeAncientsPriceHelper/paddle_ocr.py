#!/usr/bin/env python3
"""
PaddleOCR 识别脚本 - 用于 PoE2 物价助手
使用 PaddleOCR 进行中文文字识别
"""

import os
import sys
import json

# 设置环境变量，减少 PaddleOCR 的日志输出
os.environ['GLOG_minloglevel'] = '2'
os.environ['FLAGS_minloglevel'] = '2'
os.environ['PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK'] = 'True'

def setup_paddleocr():
    """初始化 PaddleOCR"""
    try:
        from paddleocr import PaddleOCR
        ocr = PaddleOCR(
            use_textline_orientation=True,
            lang='ch',
            show_log=False,
            use_gpu=False
        )
        return ocr
    except Exception as e:
        print(json.dumps({
            'success': False,
            'error': f'初始化 PaddleOCR 失败: {str(e)}',
            'items': []
        }))
        sys.exit(1)

def recognize_image(ocr, image_path):
    """识别图片中的文字"""
    try:
        if not os.path.exists(image_path):
            return {
                'success': False,
                'error': f'文件不存在: {image_path}',
                'items': []
            }
        
        file_size = os.path.getsize(image_path)
        if file_size == 0:
            return {
                'success': False,
                'error': '文件为空',
                'items': []
            }
        
        result = ocr.ocr(image_path, cls=True)
        
        items = []
        if result and len(result) > 0:
            for line in result[0]:
                if line and len(line) >= 2:
                    bbox = line[0]
                    text_info = line[1]
                    
                    if text_info and len(text_info) >= 2:
                        text = text_info[0]
                        confidence = text_info[1]
                        
                        if bbox and len(bbox) >= 4:
                            y_coords = [point[1] for point in bbox]
                            center_y = int(sum(y_coords) / len(y_coords))
                        else:
                            center_y = 0
                        
                        items.append({
                            'text': text,
                            'confidence': float(confidence),
                            'center_y': center_y
                        })
        
        return {
            'success': True,
            'items': items,
            'count': len(items)
        }
    except Exception as e:
        import traceback
        return {
            'success': False,
            'error': f'{str(e)}\n{traceback.format_exc()}',
            'items': []
        }

def main():
    if len(sys.argv) < 2:
        print(json.dumps({
            'success': False,
            'error': '用法: python paddle_ocr.py <image_path>',
            'items': []
        }))
        sys.exit(1)
    
    input_data = sys.argv[1]
    ocr = setup_paddleocr()
    result = recognize_image(ocr, input_data)
    print(json.dumps(result, ensure_ascii=False))

if __name__ == '__main__':
    main()
